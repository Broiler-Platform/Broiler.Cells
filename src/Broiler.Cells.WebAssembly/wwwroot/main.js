import { dotnet } from './_framework/dotnet.js';

const CURSORS = {
    Arrow: 'default', Text: 'text', Hand: 'pointer', Wait: 'wait', Move: 'move',
    ResizeHorizontal: 'ew-resize', ResizeVertical: 'ns-resize',
};

const EDITOR_CTRL_KEYS = new Set(['z', 'y', 'a', 's', 'o', 'n', 'w']);

let cellsExports;
let canvas;
let textInput;
let host;
let statusLine;
let started = false;
let frameRequest = 0;
let frameInProgress = false;
let observedWidth = 1000;
let observedHeight = 600;
let nativeCompositionActive = false;
let suppressNextInput = false;
const listenerCleanups = [];

function prefersDark() { return matchMedia('(prefers-color-scheme: dark)').matches; }
function prefersReducedMotion() { return matchMedia('(prefers-reduced-motion: reduce)').matches; }

function setStatus(text, failed) {
    if (!statusLine) return;
    statusLine.textContent = text;
    if (failed) statusLine.dataset.state = 'failed';
    else delete statusLine.dataset.state;
}

function showError(message) {
    const panel = document.getElementById('error-panel');
    const body = document.getElementById('error');
    if (body) body.textContent = message;
    if (panel) panel.hidden = false;
    setStatus('Managed failure — see details below.', true);
    console.error(message);
}

function scheduleFrame() {
    if (!started || frameRequest !== 0 || frameInProgress) return;
    frameRequest = requestAnimationFrame(() => {
        frameRequest = 0;
        frameInProgress = true;
        try {
            const t0 = performance.now();
            cellsExports.RenderUiFrame();
            const elapsed = performance.now() - t0;
            if (elapsed > 50) {
                console.warn(`[Cells-JS] RenderUiFrame took ${elapsed.toFixed(1)}ms`);
            }
        } catch (err) {
            console.error('[Cells-JS] Error in RenderUiFrame:', err);
            showError(err instanceof Error ? err.stack ?? err.message : String(err));
        } finally {
            frameInProgress = false;
        }
    });
}

function addListener(target, type, listener, options) {
    target.addEventListener(type, listener, options);
    listenerCleanups.push(() => target.removeEventListener(type, listener, options));
}

function localPoint(event) {
    const rect = canvas.getBoundingClientRect();
    return {
        x: (event.clientX - rect.left) * observedWidth / Math.max(1, rect.width),
        y: (event.clientY - rect.top) * observedHeight / Math.max(1, rect.height),
    };
}

function keyboardModifiers(event) {
    return (event.shiftKey ? 1 : 0) |
        (event.ctrlKey ? 2 : 0) |
        (event.altKey ? 4 : 0) |
        (event.metaKey ? 8 : 0);
}

function canonicalKey(key) {
    if (key === ' ') return 'Space';
    if (key === 'Esc') return 'Escape';
    if (key.startsWith('Arrow')) return key.slice(5);
    return key || 'Unknown';
}

function dispatchKey(event, down) {
    const key = canonicalKey(event.key);
    if (nativeCompositionActive && (event.isComposing || key === 'Process')) return;
    if ((event.ctrlKey || event.metaKey) && EDITOR_CTRL_KEYS.has(key.toLowerCase())) {
        event.preventDefault();
    } else if (!event.ctrlKey && !event.metaKey &&
        ['Tab', 'Space', 'Up', 'Down', 'Left', 'Right', 'Home', 'End', 'Backspace', 'Delete', 'Enter', 'F2'].includes(key)) {
        event.preventDefault();
    }
    cellsExports.UiKeyboardKey(
        key, down, keyboardModifiers(event), event.keyCode || event.which || 0,
        Boolean(event.repeat), Number(event.location || 0), event.timeStamp);
}

function attachInput() {
    canvas = document.getElementById('cells-canvas');
    host = document.getElementById('cells-host');
    textInput = document.getElementById('cells-text-input');
    statusLine = document.getElementById('cells-status');

    addListener(canvas, 'pointerdown', event => {
        canvas.setPointerCapture?.(event.pointerId);
        const { x, y } = localPoint(event);
        console.log(`[Cells-JS] pointerdown: (${x.toFixed(1)}, ${y.toFixed(1)}), button=${event.button}`);
        cellsExports.UiPointerButton(x, y, event.buttons, event.button, true, event.timeStamp);
        canvas.focus({ preventScroll: true });
    });

    addListener(canvas, 'pointerup', event => {
        const { x, y } = localPoint(event);
        console.log(`[Cells-JS] pointerup: (${x.toFixed(1)}, ${y.toFixed(1)}), button=${event.button}`);
        cellsExports.UiPointerButton(x, y, event.buttons, event.button, false, event.timeStamp);
    });

    addListener(canvas, 'pointermove', event => {
        const { x, y } = localPoint(event);
        cellsExports.UiPointerMove(x, y, event.buttons, event.timeStamp);
    });

    addListener(canvas, 'wheel', event => {
        event.preventDefault();
        const { x, y } = localPoint(event);
        const horizontal = Math.abs(event.deltaX) > Math.abs(event.deltaY);
        const delta = horizontal ? event.deltaX : event.deltaY;
        const notches = delta > 0 ? 1 : delta < 0 ? -1 : 0;
        cellsExports.UiPointerWheel(x, y, event.buttons, horizontal, notches, event.timeStamp);
    }, { passive: false });

    addListener(canvas, 'keydown', event => dispatchKey(event, true));
    addListener(canvas, 'keyup', event => dispatchKey(event, false));

    const resizeObserver = new ResizeObserver(entries => {
        for (const entry of entries) {
            observedWidth = Math.max(1, Math.round(entry.contentRect.width));
            observedHeight = Math.max(1, Math.round(entry.contentRect.height));
            cellsExports.ResizeUi(observedWidth, observedHeight, window.devicePixelRatio || 1);
            scheduleFrame();
        }
    });
    resizeObserver.observe(host);
    listenerCleanups.push(() => resizeObserver.disconnect());
}

const { setModuleImports, getAssemblyExports, runMain } = await dotnet.create();

setModuleImports('main.js', {
    cells: {
        openUrl: url => { window.open(url, '_blank', 'noopener,noreferrer'); },
        scheduleFrame: () => scheduleFrame(),
        setCursor: cursor => { if (canvas) canvas.style.cursor = CURSORS[cursor] || 'default'; },
        setTitle: title => { document.title = title; },
        ready: (logicalWidth, logicalHeight) => {
            attachInput();
            started = true;
            observedWidth = Math.max(1, Math.round(host.clientWidth || logicalWidth));
            observedHeight = Math.max(1, Math.round(host.clientHeight || logicalHeight));
            cellsExports.ResizeUi(observedWidth, observedHeight, window.devicePixelRatio || 1);
            scheduleFrame();
            if (statusLine) statusLine.hidden = true;
        },
        failed: (exceptionType, details) => showError(`${exceptionType}: ${details}`),
        publishFrame: () => {},
        prefersDarkScheme: () => prefersDark(),
        prefersReducedMotion: () => prefersReducedMotion(),
        requestOpenFile: accept => {},
        downloadFile: (fileName, base64Data) => {
            const a = document.createElement('a');
            a.download = fileName;
            a.href = `data:text/csv;base64,${base64Data}`;
            a.click();
        },
        promptFileName: defaultName => {
            const chosen = window.prompt('Save spreadsheet as CSV:', defaultName || 'Spreadsheet.csv');
            return chosen == null ? '' : chosen.trim();
        },
    },
});

const assemblyExports = await getAssemblyExports('Broiler.Cells.WebAssembly');
cellsExports = assemblyExports.Broiler.Cells.WebAssembly.BrowserCellsExports;

runMain().catch(error => {
    showError(error instanceof Error ? error.stack ?? error.message : String(error));
});
