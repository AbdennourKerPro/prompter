'use strict';

// ---------- settings (saved in localStorage) ----------
const STORE_KEY = 'prompter.settings.v1';
const DEFAULT_TEXT =
  'Touche le crayon en bas à gauche pour coller ton script.\n\n' +
  'Le texte défile en haut de l’écran, près de la caméra, pour que ton regard reste dans l’objectif.\n\n' +
  'Touche le texte pour lancer ou mettre en pause le défilement, fais-le glisser pour avancer ou reculer.\n\n' +
  'Le bouton rouge lance l’enregistrement (avec un compte à rebours) et le défilement en même temps.';

const defaults = {
  text: DEFAULT_TEXT,
  speed: 40,          // pixels per second
  fontSize: 34,       // px
  height: 45,         // % of the screen covered by the prompter
  bgOpacity: 0.55,
  margin: 6,          // % side margins
  center: false,
  mirror: false,
  autoScroll: true,
  countdown: true,
  facing: 'user',
  quality: '1080',
};

function loadSettings() {
  try {
    return { ...defaults, ...JSON.parse(localStorage.getItem(STORE_KEY) || '{}') };
  } catch (e) {
    return { ...defaults };
  }
}
function saveSettings() {
  try { localStorage.setItem(STORE_KEY, JSON.stringify(s)); } catch (e) { /* private mode */ }
}

const s = loadSettings();
const $ = (sel) => document.querySelector(sel);

const els = {
  preview: $('#preview'),
  prompter: $('#prompter'),
  script: $('#script'),
  marker: $('#marker'),
  recBadge: $('#rec-badge'),
  recTime: $('#rec-time'),
  countdown: $('#countdown'),
  toast: $('#toast'),
  speedLabel: $('#speed-label'),
  settings: $('#settings'),
  editor: $('#editor'),
  scriptInput: $('#script-input'),
  result: $('#result'),
  resultVideo: $('#result-video'),
  resultInfo: $('#result-info'),
  share: $('#btn-share'),
  download: $('#btn-download'),
  start: $('#start'),
  startError: $('#start-error'),
  camInfo: $('#cam-info'),
};

// ---------- toast ----------
let toastTimer = 0;
function toast(msg, ms = 2200) {
  els.toast.textContent = msg;
  els.toast.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { els.toast.hidden = true; }, ms);
}

// ---------- prompter rendering ----------
const MARKER_RATIO = 0.18; // reading line, as a fraction of the prompter height
let pos = 0;               // scroll offset in px
let playing = false;

function applyLayout() {
  const root = document.documentElement.style;
  root.setProperty('--font-size', s.fontSize + 'px');
  root.setProperty('--prompter-height', s.height + '%');
  root.setProperty('--bg-opacity', s.bgOpacity);
  root.setProperty('--margin', s.margin + '%');
  els.script.classList.toggle('center', s.center);
  // First line sits on the reading marker.
  const markerY = els.marker.offsetTop + els.marker.offsetHeight / 2;
  els.script.style.paddingTop = Math.max(0, markerY - s.fontSize * 0.68) + 'px';
  els.speedLabel.textContent = 'Vitesse ' + s.speed;
  clampPos();
  applyTransform();
}

function maxPos() {
  // Last line can scroll up to the reading marker.
  const cs = getComputedStyle(els.script);
  const content = els.script.scrollHeight - parseFloat(cs.paddingTop) - parseFloat(cs.paddingBottom);
  return Math.max(0, content - s.fontSize * 1.35);
}
function clampPos() { pos = Math.min(Math.max(0, pos), maxPos()); }

function applyTransform() {
  els.script.style.transform =
    `translate3d(0, ${-pos}px, 0)` + (s.mirror ? ' scaleX(-1)' : '');
}

function setText(text) {
  s.text = text;
  els.script.textContent = text;
  saveSettings();
  applyLayout();
}

function setPlaying(on) {
  playing = on && s.speed > 0;
  document.body.classList.toggle('playing', playing);
  if (playing && pos >= maxPos()) pos = 0; // restart from the top if at the end
}

function rewind() { pos = 0; applyTransform(); }

let lastFrame = 0;
function frame(t) {
  const dt = Math.min((t - lastFrame) / 1000, 0.1);
  lastFrame = t;
  if (playing && !drag.active) {
    pos += s.speed * dt;
    if (pos >= maxPos()) { pos = maxPos(); setPlaying(false); }
    applyTransform();
  }
  requestAnimationFrame(frame);
}
requestAnimationFrame((t) => { lastFrame = t; requestAnimationFrame(frame); });

// Tap the text to play/pause, drag it to scroll by hand.
const drag = { active: false, moved: false, startY: 0, startPos: 0, id: null };
els.prompter.addEventListener('pointerdown', (e) => {
  drag.active = true;
  drag.moved = false;
  drag.startY = e.clientY;
  drag.startPos = pos;
  drag.id = e.pointerId;
  els.prompter.setPointerCapture(e.pointerId);
});
els.prompter.addEventListener('pointermove', (e) => {
  if (!drag.active || e.pointerId !== drag.id) return;
  const dy = e.clientY - drag.startY;
  if (Math.abs(dy) > 8) drag.moved = true;
  if (drag.moved) {
    pos = drag.startPos - dy;
    clampPos();
    applyTransform();
  }
});
function endDrag(e) {
  if (!drag.active || e.pointerId !== drag.id) return;
  drag.active = false;
  if (!drag.moved && e.type === 'pointerup') setPlaying(!playing);
}
els.prompter.addEventListener('pointerup', endDrag);
els.prompter.addEventListener('pointercancel', endDrag);

// ---------- speed ----------
function changeSpeed(d) {
  s.speed = Math.min(300, Math.max(5, s.speed + d));
  saveSettings();
  syncSettingsUI();
  applyLayout();
}
$('#speed-down').addEventListener('click', () => changeSpeed(-5));
$('#speed-up').addEventListener('click', () => changeSpeed(+5));

// ---------- camera ----------
let stream = null;

function stopCamera() {
  if (stream) stream.getTracks().forEach((t) => t.stop());
  stream = null;
}

async function startCamera() {
  if (!window.isSecureContext || !navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
    throw new Error('La caméra n’est disponible qu’en HTTPS (ex. GitHub Pages). Ouvre la page avec une adresse https://');
  }
  stopCamera();
  const [w, h] = s.quality === '720' ? [1280, 720] : [1920, 1080];
  const portrait = window.innerHeight > window.innerWidth;
  const video = {
    facingMode: s.facing,
    width: { ideal: portrait ? h : w },
    height: { ideal: portrait ? w : h },
    frameRate: { ideal: 30 },
  };
  const audio = { echoCancellation: true, noiseSuppression: true };
  try {
    stream = await navigator.mediaDevices.getUserMedia({ video, audio });
  } catch (e) {
    if (e.name === 'NotAllowedError') {
      throw new Error('Accès caméra / micro refusé. Autorise-le dans les réglages du navigateur puis recharge la page.');
    }
    // Retry with looser constraints (some phones reject the resolution).
    stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: s.facing }, audio: true });
  }
  els.preview.srcObject = stream;
  els.preview.classList.toggle('mirrored', s.facing === 'user');
  await els.preview.play().catch(() => {});
  const st = stream.getVideoTracks()[0].getSettings();
  els.camInfo.textContent = st.width ? `Caméra : ${st.width}×${st.height}` + (st.frameRate ? `, ${Math.round(st.frameRate)} i/s` : '') : '';
}

$('#btn-start').addEventListener('click', async () => {
  els.startError.textContent = '';
  try {
    await startCamera();
    els.start.hidden = true;
    requestWakeLock();
    applyLayout();
  } catch (e) {
    els.startError.textContent = e.message || String(e);
  }
});

// Re-open the camera for the new orientation (not while recording).
let orientTimer = 0;
window.addEventListener('resize', () => {
  applyLayout();
  clearTimeout(orientTimer);
  orientTimer = setTimeout(() => {
    if (stream && !recorder && els.start.hidden) startCamera().catch((e) => toast(e.message));
  }, 600);
});

// iOS can stop the camera when the app goes to the background.
document.addEventListener('visibilitychange', () => {
  if (document.visibilityState !== 'visible' || !els.start.hidden) return;
  requestWakeLock();
  const track = stream && stream.getVideoTracks()[0];
  if (!recorder && (!track || track.readyState === 'ended')) startCamera().catch((e) => toast(e.message));
});

// ---------- screen wake lock ----------
let wakeLock = null;
async function requestWakeLock() {
  try {
    if ('wakeLock' in navigator && !wakeLock) {
      wakeLock = await navigator.wakeLock.request('screen');
      wakeLock.addEventListener('release', () => { wakeLock = null; });
    }
  } catch (e) { /* not supported or denied */ }
}

// ---------- recording ----------
let recorder = null;
let chunks = [];
let recStart = 0;
let recTimer = 0;
let lastBlobUrl = null;
let lastFile = null;
let countingDown = false;

function pickMimeType() {
  const candidates = [
    'video/mp4;codecs=avc1.42E01E,mp4a.40.2',
    'video/mp4;codecs=avc1,mp4a',
    'video/mp4',
    'video/webm;codecs=vp9,opus',
    'video/webm;codecs=vp8,opus',
    'video/webm',
  ];
  for (const m of candidates) {
    try { if (MediaRecorder.isTypeSupported(m)) return m; } catch (e) { /* ignore */ }
  }
  return '';
}

function fmtTime(sec) {
  const m = Math.floor(sec / 60);
  const ss = Math.floor(sec % 60);
  return `${m}:${String(ss).padStart(2, '0')}`;
}

function sleep(ms) { return new Promise((r) => setTimeout(r, ms)); }

async function runCountdown() {
  countingDown = true;
  els.countdown.hidden = false;
  for (let i = 3; i > 0; i--) {
    els.countdown.textContent = i;
    await sleep(1000);
    if (!countingDown) break;
  }
  els.countdown.hidden = true;
  const ok = countingDown;
  countingDown = false;
  return ok;
}

async function startRecording() {
  if (!stream) { toast('Caméra non active'); return; }
  if (!window.MediaRecorder) { toast('Enregistrement vidéo non supporté par ce navigateur'); return; }
  if (s.autoScroll) { setPlaying(false); rewind(); }
  document.body.classList.add('recording');
  if (s.countdown && !(await runCountdown())) { document.body.classList.remove('recording'); return; }

  const mimeType = pickMimeType();
  const opts = {
    videoBitsPerSecond: s.quality === '720' ? 5_000_000 : 8_000_000,
    audioBitsPerSecond: 128_000,
  };
  if (mimeType) opts.mimeType = mimeType;
  try {
    recorder = new MediaRecorder(stream, opts);
  } catch (e) {
    recorder = new MediaRecorder(stream);
  }
  chunks = [];
  recorder.ondataavailable = (e) => { if (e.data && e.data.size) chunks.push(e.data); };
  recorder.onstop = onRecordingStopped;
  recorder.onerror = (e) => toast('Erreur d’enregistrement : ' + (e.error && e.error.message || ''));
  recorder.start(1000);

  recStart = performance.now();
  els.recTime.textContent = '0:00';
  els.recBadge.hidden = false;
  recTimer = setInterval(() => {
    els.recTime.textContent = fmtTime((performance.now() - recStart) / 1000);
  }, 250);
  if (s.autoScroll) setPlaying(true);
  requestWakeLock();
}

function stopRecording() {
  if (countingDown) { countingDown = false; return; }
  if (recorder && recorder.state !== 'inactive') recorder.stop();
  setPlaying(false);
}

function onRecordingStopped() {
  clearInterval(recTimer);
  els.recBadge.hidden = true;
  document.body.classList.remove('recording');
  const type = (recorder.mimeType || chunks[0] && chunks[0].type || 'video/webm').split(';')[0];
  const duration = (performance.now() - recStart) / 1000;
  recorder = null;
  const blob = new Blob(chunks, { type });
  chunks = [];
  showResult(blob, type, duration);
}

function showResult(blob, type, duration) {
  if (lastBlobUrl) URL.revokeObjectURL(lastBlobUrl);
  lastBlobUrl = URL.createObjectURL(blob);
  const ext = type.includes('mp4') ? 'mp4' : 'webm';
  const d = new Date();
  const p = (n) => String(n).padStart(2, '0');
  const name = `prompteur-${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}-${p(d.getHours())}${p(d.getMinutes())}${p(d.getSeconds())}.${ext}`;
  lastFile = new File([blob], name, { type });

  els.resultVideo.src = lastBlobUrl;
  els.download.href = lastBlobUrl;
  els.download.download = name;
  els.resultInfo.textContent = `${name} · ${fmtTime(duration)} · ${(blob.size / 1e6).toFixed(1)} Mo`;
  els.share.hidden = !(navigator.canShare && navigator.canShare({ files: [lastFile] }));
  els.result.hidden = false;
}

$('#btn-rec').addEventListener('click', () => {
  if (recorder || countingDown) stopRecording(); else startRecording();
});

els.share.addEventListener('click', async () => {
  try {
    await navigator.share({ files: [lastFile], title: lastFile.name });
  } catch (e) {
    if (e.name !== 'AbortError') toast('Partage impossible, utilise « Télécharger »');
  }
});

// ---------- buttons & sheets ----------
$('#btn-play').addEventListener('click', () => setPlaying(!playing));
$('#btn-rewind').addEventListener('click', rewind);

function openSheet(el) {
  [els.settings, els.editor].forEach((x) => { if (x !== el) x.hidden = true; });
  el.hidden = false;
}
document.querySelectorAll('[data-close]').forEach((b) => {
  b.addEventListener('click', () => {
    const el = document.getElementById(b.dataset.close);
    el.hidden = true;
    if (el === els.result) els.resultVideo.pause();
  });
});

$('#btn-settings').addEventListener('click', () => {
  if (els.settings.hidden) openSheet(els.settings); else els.settings.hidden = true;
});

$('#btn-edit').addEventListener('click', () => {
  if (recorder) { toast('Arrête l’enregistrement pour modifier le script'); return; }
  setPlaying(false);
  els.scriptInput.value = s.text;
  openSheet(els.editor);
  els.scriptInput.focus();
});
$('#btn-save-script').addEventListener('click', () => {
  setText(els.scriptInput.value);
  els.editor.hidden = true;
  rewind();
});

// ---------- settings sheet ----------
const ranges = [
  ['#r-speed', '#o-speed', 'speed', (v) => v],
  ['#r-font', '#o-font', 'fontSize', (v) => v + ' px'],
  ['#r-height', '#o-height', 'height', (v) => v + ' %'],
  ['#r-bg', '#o-bg', 'bgOpacity', (v) => Math.round(v * 100) + ' %'],
  ['#r-margin', '#o-margin', 'margin', (v) => v + ' %'],
];
const checks = [
  ['#c-center', 'center'],
  ['#c-mirror', 'mirror'],
  ['#c-auto', 'autoScroll'],
  ['#c-count', 'countdown'],
];

function syncSettingsUI() {
  for (const [r, o, key, fmt] of ranges) {
    $(r).value = s[key];
    $(o).textContent = fmt(s[key]);
  }
  for (const [c, key] of checks) $(c).checked = !!s[key];
  $('#s-facing').value = s.facing;
  $('#s-quality').value = s.quality;
}

for (const [r, o, key, fmt] of ranges) {
  $(r).addEventListener('input', (e) => {
    s[key] = Number(e.target.value);
    $(o).textContent = fmt(s[key]);
    saveSettings();
    applyLayout();
  });
}
for (const [c, key] of checks) {
  $(c).addEventListener('change', (e) => {
    s[key] = e.target.checked;
    saveSettings();
    applyLayout();
  });
}
for (const [sel, key] of [['#s-facing', 'facing'], ['#s-quality', 'quality']]) {
  $(sel).addEventListener('change', (e) => {
    if (recorder) { e.target.value = s[key]; toast('Impossible pendant l’enregistrement'); return; }
    s[key] = e.target.value;
    saveSettings();
    if (stream) startCamera().catch((err) => toast(err.message));
  });
}

// ---------- keyboard (Bluetooth remote / computer) ----------
document.addEventListener('keydown', (e) => {
  if (!els.editor.hidden || e.target.tagName === 'INPUT' || e.target.tagName === 'SELECT') return;
  switch (e.key) {
    case ' ': case 'Enter': case 'PageDown': setPlaying(!playing); break;
    case 'ArrowUp': changeSpeed(+5); break;
    case 'ArrowDown': changeSpeed(-5); break;
    case 'ArrowLeft': pos -= s.fontSize * 1.35; clampPos(); applyTransform(); break;
    case 'ArrowRight': pos += s.fontSize * 1.35; clampPos(); applyTransform(); break;
    case 'Home': rewind(); break;
    case 'r': case 'R': $('#btn-rec').click(); break;
    default: return;
  }
  e.preventDefault();
});

// Warn before losing an unsaved recording.
window.addEventListener('beforeunload', (e) => {
  if (recorder || (lastFile && !els.result.hidden)) { e.preventDefault(); e.returnValue = ''; }
});

// ---------- init ----------
els.script.textContent = s.text;
syncSettingsUI();
applyLayout();
if (document.fonts && document.fonts.ready) document.fonts.ready.then(applyLayout);
