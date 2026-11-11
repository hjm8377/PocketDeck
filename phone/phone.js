'use strict';

const deck = document.querySelector('#deck');
const status = document.querySelector('#status');
const pair = document.querySelector('#pair');
const codeInput = document.querySelector('#code');
const template = document.querySelector('#button-template');
const storageKey = 'pocket-deck-code';
let code = location.hash.slice(1).toUpperCase();
let version;
let loading = false;
let noticeUntil = 0;

try {
  code ||= localStorage.getItem(storageKey) || '';
  if (location.hash) history.replaceState(null, '', '/');
} catch { /* A private browsing session can still pair for the current page. */ }

function setStatus(message, state) {
  status.textContent = message;
  status.dataset.state = state;
}

function showError(message) {
  noticeUntil = Date.now() + 3500;
  setStatus(message, 'error');
}

function request(path, method = 'GET') {
  const abort = new AbortController();
  const timeout = setTimeout(() => abort.abort(), 3500);
  return fetch(path, {
    method,
    headers: { 'X-Deck-Code': code },
    cache: 'no-store',
    signal: abort.signal
  }).finally(() => clearTimeout(timeout));
}

function unpair() {
  code = '';
  version = undefined;
  pair.hidden = false;
  deck.hidden = true;
  try { localStorage.removeItem(storageKey); } catch {}
  showError('PC에 표시된 연결 코드를 다시 입력해 주세요.');
}

function draw(layout) {
  deck.style.setProperty('--columns', layout.columns);
  deck.style.setProperty('--rows', layout.rows);
  deck.replaceChildren(...layout.buttons.map((button, index) => {
    const tile = template.content.firstElementChild.cloneNode(true);
    const label = button.label || '';
    const icon = button.icon || '';
    const background = /^#[\da-f]{6}$/i.test(button.color) ? button.color : '#39455d';
    const rgb = background.slice(1).match(/../g).map(channel => parseInt(channel, 16));
    const light = rgb[0] * 0.299 + rgb[1] * 0.587 + rgb[2] * 0.114 > 158;
    tile.style.setProperty('--button-bg', background);
    tile.style.setProperty('--button-fg', light ? '#111827' : '#ffffff');
    tile.querySelector('.tile-label').textContent = label;
    tile.querySelector('.tile-icon').textContent = icon;
    tile.setAttribute('aria-label', label || icon || `${index + 1}번 버튼`);
    tile.disabled = !button.enabled;
    tile.addEventListener('click', () => press(index, layout.version));
    return tile;
  }));
}

async function refresh() {
  if (!code || loading) return;
  loading = true;
  try {
    const response = await request('/api/layout');
    if (response.status === 401) return unpair();
    if (!response.ok) throw new Error('layout request failed');
    const layout = await response.json();
    if (layout.version !== version) {
      draw(layout);
      version = layout.version;
    }
    pair.hidden = true;
    deck.hidden = false;
    try { localStorage.setItem(storageKey, code); } catch {}
    if (Date.now() >= noticeUntil) setStatus('PC 연결됨', 'ready');
  } catch {
    setStatus('PC 앱과 네트워크 연결을 확인해 주세요.', 'error');
  } finally {
    loading = false;
  }
}

async function press(index, buttonVersion) {
  try {
    const response = await request(`/api/press/${buttonVersion}/${index}`, 'POST');
    if (response.status === 401) return unpair();
    if (response.status === 409 || response.status === 422) {
      showError(await response.text());
      if (response.status === 409) await refresh();
    } else if (!response.ok) {
      throw new Error('button request failed');
    }
  } catch {
    showError('버튼 동작을 실행하지 못했어요.');
  }
}

pair.addEventListener('submit', event => {
  event.preventDefault();
  if (loading) return;
  code = codeInput.value.trim().toUpperCase();
  noticeUntil = 0;
  setStatus('PC에 연결 중', 'pending');
  refresh();
});

if (code) {
  codeInput.value = code;
  refresh();
}
setInterval(() => { if (!document.hidden) refresh(); }, 2000);
document.addEventListener('visibilitychange', () => { if (!document.hidden) refresh(); });
