import { OfficeRecovery } from './broiler.recovery.js';
export const officeInputProtocolVersion = 1;
if (location.origin === 'https://broiler-platform.github.io') document.querySelector('.office-nav a').href = '/demo/';
import { dotnet } from './_framework/dotnet.js';

const CURSORS = {
    Arrow: 'default', Text: 'text', Hand: 'pointer', Wait: 'wait', Move: 'move',
    ResizeHorizontal: 'ew-resize', ResizeVertical: 'ns-resize',
};

const EDITOR_CTRL_KEYS = new Set(['z', 'y', 'a', 's', 'o', 'n', 'w']);

let cellsExports;
let recovery;
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
let compositionEcho = null, compositionEchoTimer;
let framePending = false;
let modified = false, generation = 0, revision = 0, fileBusy = false, filePicker;
let pendingAction = null;
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
    if (!started) return;
    if (frameInProgress) { framePending = true; return; }
    if (frameRequest !== 0) return;
    frameRequest = requestAnimationFrame(() => {
        frameRequest = 0;
        frameInProgress = true;
        try {
            cellsExports.RenderUiFrame();
        } catch (err) {
            console.error('[Cells-JS] Error in RenderUiFrame:', err);
            showError(err instanceof Error ? err.stack ?? err.message : String(err));
        } finally {
            frameInProgress = false;
            if (framePending) { framePending = false; scheduleFrame(); }
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
        (event.ctrlKey || event.metaKey ? 2 : 0) |
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
    if ((event.ctrlKey || event.metaKey) && ["c", "x", "v"].includes(key.toLowerCase())) {
        if (down && key.toLowerCase() !== 'v') {
            // Native WebKit copy/cut needs a real DOM selection even for an owner-drawn cell.
            textInput.value = cellsExports.SelectedCellText(); textInput.select();
        }
        return;
    }
    if (nativeCompositionActive && (event.isComposing || key === 'Process')) return;
    if ((event.ctrlKey || event.metaKey) && EDITOR_CTRL_KEYS.has(key.toLowerCase())) {
        event.preventDefault();
    } else if (!event.ctrlKey && !event.metaKey &&
        (key === 'Space' ? event.target !== textInput :
            ['Tab', 'Up', 'Down', 'Left', 'Right', 'Home', 'End', 'Backspace', 'Delete', 'Enter', 'F2'].includes(key))) {
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
    setupTextInput();

    addListener(canvas, 'pointerdown', event => {
        try { canvas.setPointerCapture?.(event.pointerId); } catch { }
        const { x, y } = localPoint(event);
        cellsExports.UiPointerButton(x, y, event.buttons, event.button, true, event.timeStamp);
        canvas.focus({ preventScroll: true });
    });

    addListener(canvas, 'pointerup', event => {
        const { x, y } = localPoint(event);
        cellsExports.UiPointerButton(x, y, event.buttons, event.button, false, event.timeStamp);
    });

    for (const type of ['pointercancel', 'lostpointercapture']) addListener(canvas, type, event => cellsExports.UiCancelPointer(event.timeStamp));
    addListener(window, 'blur', event => { cellsExports.UiCancelPointer(event.timeStamp); if (nativeCompositionActive) { nativeCompositionActive = false; cellsExports.UiTextComposition('', 3, event.timeStamp); } });

    addListener(canvas, 'pointermove', event => {
        const { x, y } = localPoint(event);
        cellsExports.UiPointerMove(x, y, event.buttons, event.timeStamp);
    });

    addListener(canvas, 'wheel', event => {
        event.preventDefault();
        const { x, y } = localPoint(event);
        const horizontal = Math.abs(event.deltaX) > Math.abs(event.deltaY);
        const delta = horizontal ? event.deltaX : event.deltaY;
        const unit = event.deltaMode === 0 ? 100 : event.deltaMode === 1 ? 3 : 1;
        const notches = -delta / unit;
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

function setupTextInput() {
    addListener(textInput, 'keydown', event => dispatchKey(event, true));
    addListener(textInput, 'keyup', event => dispatchKey(event, false));
    addListener(textInput, 'beforeinput', event => {
        if (event.isComposing || nativeCompositionActive || (event.inputType || '').includes('Composition')) return;
        if (event.inputType === 'insertText' && event.data) {
            event.preventDefault();
            if (compositionEcho !== null && compositionEcho === event.data) compositionEcho = null;
            else cellsExports.UiTextInput(event.data, event.timeStamp);
        } else if ((event.inputType || '').startsWith('delete')) {
            event.preventDefault();
        }
    });
    addListener(textInput, 'input', event => {
        if (compositionEcho !== null && compositionEcho === event.data) { compositionEcho = null; textInput.value = ''; return; }
        if (!nativeCompositionActive && !event.isComposing && event.data)
            cellsExports.UiTextInput(event.data, event.timeStamp);
        if (!nativeCompositionActive) textInput.value = '';
    });
    addListener(textInput, 'compositionstart', event => {
        nativeCompositionActive = true;
        cellsExports.UiTextComposition('', 0, event.timeStamp);
    });
    addListener(textInput, 'compositionupdate', event => {
        const start = textInput.selectionStart ?? 0;
        cellsExports.UiTextComposition(event.data ?? '', 1, event.timeStamp);
    });
    addListener(textInput, 'compositionend', event => {
        nativeCompositionActive = false;
        compositionEcho = event.data ?? '';
        clearTimeout(compositionEchoTimer);
        compositionEchoTimer = setTimeout(() => { compositionEcho = null; }, 0);
        cellsExports.UiTextComposition(event.data ?? '', 2, event.timeStamp);
        textInput.value = '';
    });
    addListener(textInput, 'blur', event => {
        if (!nativeCompositionActive) return;
        nativeCompositionActive = false;
        cellsExports.UiTextComposition('', 3, event.timeStamp);
    });
    for (const operation of ['copy', 'cut', 'paste']) {
        for (const target of [textInput, canvas]) addListener(target, operation, event => {
            if (!event.isTrusted) return;
            if (!event.clipboardData) { reportOperation('Failed', 'Browser clipboard data is unavailable; sheet retained.'); return; }
            const incoming = operation === 'paste' ? event.clipboardData?.getData('text/plain') ?? '' : '';
            const outgoing = cellsExports.UiClipboardEvent(operation, incoming);
            if (operation === 'paste') {
                event.preventDefault();
            } else {
                event.clipboardData?.setData('text/plain', outgoing);
                // Keep the native copy/cut default: the selected backing textarea
                // contains exactly this cell and WebKit publishes that selection.
            }
            if (operation === 'paste') textInput.value = '';
            else setTimeout(() => { if (!nativeCompositionActive) textInput.value = ''; }, 0);
        });
    }
}

// ---- Browser-native open / save ------------------------------------------------------------------

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
            queueMicrotask(() => recovery.start());
        },
        failed: (exceptionType, details) => showError(`${exceptionType}: ${details}`),
        publishFrame: (frameIndex, active, x, y, width, height) => {
            if (!started || !textInput) return;
            canvas.dataset.frame = String(frameIndex);
            textInput.dataset.caretActive = String(active);
            if (active) Object.assign(textInput.style, { left: `${Math.max(0,x)}px`, top: `${Math.max(0,y)}px`, width: `${Math.max(1,width)}px`, height: `${Math.max(8,height)}px` });
            // Grid needs committed text even before a cell editor owns the caret.
            if (document.activeElement === canvas) textInput.focus({ preventScroll: true });
        },
        prefersDarkScheme: () => prefersDark(),
        prefersReducedMotion: () => prefersReducedMotion(),
        requestOpenFile,
        requestAction,
        documentState: (name, dirty, gen, rev) => {
            recovery?.changed({name, modified:dirty, generation:gen, revision:rev});
            generation = gen; revision = rev;
            document.body.dataset.modified = String(dirty);
            document.body.dataset.revision = String(rev);
            document.getElementById('cells-name').textContent = name + (dirty ? ' · Modified' : '');
            if (modified !== dirty) {
                modified = dirty; window.removeEventListener('beforeunload', beforeUnload);
                if (dirty) window.addEventListener('beforeunload', beforeUnload);
            }
        },
        reportOperation,

    },
});

const assemblyExports = await getAssemblyExports('Broiler.Cells.WebAssembly');
cellsExports = assemblyExports.Broiler.Cells.WebAssembly.BrowserCellsExports;
recovery = new OfficeRecovery({
    app: 'cells',
    capture: () => JSON.parse(cellsExports.RecoverySnapshot()),
    restore: record => { if (!cellsExports.RestoreRecovery(record.name, record.payload)) throw new Error('The draft could not be restored; current content retained.'); },
    acceptFormat: format => format === 'cells-json',
});


runMain().catch(error => {
    showError(error instanceof Error ? error.stack ?? error.message : String(error));
});

function beforeUnload(event) { event.preventDefault(); event.returnValue = ''; }
function reportOperation(outcome, message) {
    const status = document.getElementById('cells-operation'); status.dataset.outcome = outcome; status.textContent = message; status.title = message;
}
function requestAction(action, dirty, name) {
    if (fileBusy || pendingAction) { reportOperation('Failed', 'Finish the current file operation first.'); return; }
    if (action.startsWith('export')) { exportSheet(name); return; }
    if (dirty) {
        pendingAction = action;
        const dialog = document.getElementById('cells-confirm'); dialog.returnValue = ''; dialog.showModal();
    } else finishAction(action);
}
function finishAction(action) {
    if (action === 'exit') { window.removeEventListener('beforeunload', beforeUnload); location.href = 'https://demo.broiler.app/'; return; }
    cellsExports.CompleteAction(action);
}
document.getElementById('cells-confirm').addEventListener('close', event => {
    const action = pendingAction; pendingAction = null;
    if (event.target.returnValue === 'discard') finishAction(action);
    else if (event.target.returnValue === 'export') exportSheet(document.getElementById('cells-name').textContent.split(' ·')[0]);
    else reportOperation('Canceled', 'Action canceled; sheet retained.');
});
function exportSheet(name) {
    const dialog = document.getElementById('cells-export'); document.getElementById('cells-filename').value = name || 'Spreadsheet.csv';
    fileBusy = true; dialog.returnValue = ''; dialog.showModal();
}
document.getElementById('cells-export').addEventListener('close', async event => {
    try {
        if (event.target.returnValue !== 'export') { reportOperation('Canceled', 'Export canceled; sheet retained.'); return; }
        const prepared = cellsExports.PrepareExport(document.getElementById('cells-filename').value);
        if (!prepared) return;
        const [name, base64] = prepared.split('\0');
        const binary = atob(base64), bytes = Uint8Array.from(binary, char => char.charCodeAt(0));
        const url = URL.createObjectURL(new Blob([bytes], { type: 'text/csv;charset=utf-8' }));
        const anchor = document.createElement('a'); anchor.href = url; anchor.download = name;
        document.body.append(anchor); anchor.click(); anchor.remove(); setTimeout(() => URL.revokeObjectURL(url), 60000);
        cellsExports.ExportCompleted('ExportInitiated', '');
    } catch (error) { cellsExports.ExportCompleted('Failed', String(error)); }
    finally { fileBusy = false; }
});
function requestOpenFile(accept) {
    if (fileBusy) return;
    fileBusy = true; const selectedGeneration = generation, selectedRevision = revision;
    filePicker?.remove(); filePicker = document.createElement('input'); filePicker.type = 'file'; filePicker.accept = accept; filePicker.hidden = true; document.body.append(filePicker);
    filePicker.addEventListener('cancel', () => { fileBusy = false; reportOperation('Canceled', 'Open canceled; sheet retained.'); }, { once: true });
    filePicker.addEventListener('change', async () => {
        try {
            const file = filePicker.files?.[0]; if (!file) { reportOperation('Canceled', 'Open canceled; sheet retained.'); return; }
            if (file.size > 2 * 1024 * 1024) throw new Error('CSV exceeds the 2 MiB file limit.');
            const bytes = new Uint8Array(await file.arrayBuffer());
            if (generation !== selectedGeneration || revision !== selectedRevision) throw new Error('The sheet changed while the picker was open.');
            let binary = ''; for (let offset=0; offset<bytes.length; offset+=32768) binary += String.fromCharCode(...bytes.subarray(offset,offset+32768));
            cellsExports.LoadDocument(file.name, btoa(binary));
        } catch (error) { reportOperation('Failed', 'Open failed; sheet retained. ' + error); }
        finally { fileBusy = false; }
    }, { once: true });
    try { filePicker.click(); } catch (error) { fileBusy = false; reportOperation('Failed', String(error)); }
}
window.addEventListener('pagehide', () => {
    started = false; cancelAnimationFrame(frameRequest); for (const cleanup of listenerCleanups.splice(0)) cleanup();
    clearTimeout(compositionEchoTimer); filePicker?.remove(); window.removeEventListener('beforeunload', beforeUnload); cellsExports.Dispose();
}, { once: true });
window.addEventListener('pageshow', event => { if (event.persisted) location.reload(); });

// Read-only model export for host integrations and diagnostics.
export function sheetSnapshot() { return JSON.parse(cellsExports.SheetSnapshot()); }

// Read-only host diagnostics; mutation remains in the editor and recovery commands.
export function recoverySnapshot() { return JSON.parse(cellsExports.RecoverySnapshot()); }
