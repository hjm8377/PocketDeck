'use strict';

const deck = document.querySelector('#deck');
const status = document.querySelector('#status');
const pair = document.querySelector('#pair');
const codeInput = document.querySelector('#code');
const template = document.querySelector('#button-template');
const storageKey = 'pocket-deck-code';
let code = location.hash.slice(1);
let version;
let loading = false;
let noticeUntil = 0;
let clocks = [];
let weatherTiles = [];
let mixerViews = [];
let readingMixer = false;
let mixerRevision = 0;
const weatherCache = new Map();
const timeFormat = new Intl.DateTimeFormat('ko-KR', {
  hour: '2-digit', minute: '2-digit', hourCycle: 'h23'
});

try {
  code ||= localStorage.getItem(storageKey) || '';
  if (location.hash) history.replaceState(null, '', '/');
} catch { /* A private browsing session can still pair for the current page. */ }
if (!/^[0-9]{8}$/.test(code)) code = '';

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
  clocks = [];
  weatherTiles = [];
  mixerViews = [];
  try { localStorage.removeItem(storageKey); } catch {}
  showError('PC에 표시된 연결 코드를 다시 입력해 주세요.');
}

function draw(layout) {
  deck.style.setProperty('--columns', layout.columns);
  deck.style.setProperty('--rows', layout.rows);
  clocks = [];
  weatherTiles = [];
  mixerViews = [];
  const tiles = [];
  layout.buttons.forEach((button, index) => {
    if (button.covered) return;
    const tile = button.widget
      ? createWidget(button)
      : template.content.firstElementChild.cloneNode(true);
    const label = button.label || '';
    const icon = button.icon || '';
    const background = /^#[\da-f]{6}$/i.test(button.color) ? button.color : '#39455d';
    const rgb = background.slice(1).match(/../g).map(channel => parseInt(channel, 16));
    const light = rgb[0] * 0.299 + rgb[1] * 0.587 + rgb[2] * 0.114 > 158;
    tile.style.setProperty('--button-bg', background);
    tile.style.setProperty('--button-fg', light ? '#111827' : '#ffffff');
    tile.style.gridColumn = `${index % layout.columns + 1} / span ${button.width || 1}`;
    tile.style.gridRow = `${Math.floor(index / layout.columns) + 1} / span ${button.height || 1}`;
    tiles.push(tile);
    if (button.widget) return;
    tile.querySelector('.tile-label').textContent = label;
    const iconSlot = tile.querySelector('.tile-icon');
    iconSlot.textContent = icon || (button.appIcon ? '↗' : '');
    if (button.appIcon) showAppIcon(iconSlot, layout.version, index);
    tile.setAttribute('aria-label', label || icon || `${index + 1}번 버튼`);
    tile.disabled = !button.enabled;
    addPressFeedback(tile);
    tile.addEventListener('click', () => press(index, layout.version));
  });
  deck.replaceChildren(...tiles);
  updateClocks();
  updateWeatherTiles();
  refreshMixer();
}

function addPressFeedback(tile) {
  const release = () => tile.classList.remove('is-pressed');
  tile.addEventListener('pointerdown', event => {
    if (!tile.disabled && event.button === 0) tile.classList.add('is-pressed');
  });
  for (const event of ['pointerup', 'pointercancel', 'pointerleave', 'lostpointercapture', 'blur'])
    tile.addEventListener(event, release);
}

function createWidget(button) {
  if (button.widget === 'mixer') return createMixer(button);
  const tile = document.createElement('section');
  tile.className = 'tile widget';
  tile.setAttribute('aria-label', button.label || (button.widget === 'clock' ? '시계' : '날씨'));
  const value = document.createElement('div');
  value.className = 'widget-value';
  const label = document.createElement('div');
  label.className = 'tile-label';
  label.textContent = button.label;
  tile.append(value, label);
  if (button.widget === 'clock') {
    clocks.push(value);
    return tile;
  }
  const source = document.createElement('a');
  source.className = 'widget-source';
  source.href = 'https://open-meteo.com/';
  source.target = '_blank';
  source.rel = 'noopener';
  source.textContent = 'Open-Meteo';
  tile.append(source);
  value.textContent = '—';
  weatherTiles.push({ tile, value, label, location: button.location });
  return tile;
}

function createMixer(button) {
  const tile = document.createElement('section');
  tile.className = 'tile widget mixer';
  tile.setAttribute('aria-label', button.label || '볼륨 믹서');
  const select = document.createElement('select');
  select.setAttribute('aria-label', '볼륨을 조절할 앱');
  const slider = document.createElement('input');
  slider.type = 'range';
  slider.min = '0';
  slider.max = '100';
  slider.setAttribute('aria-label', '볼륨');
  const value = document.createElement('output');
  const control = document.createElement('div');
  control.className = 'mixer-control';
  control.append(slider, value);
  tile.append(select, control);
  select.disabled = slider.disabled = true;
  select.add(new Option('볼륨 믹서', ''));
  value.textContent = '—';
  const view = { tile, select, slider, value, channels: [], dragging: false, pending: null, sending: false };
  mixerViews.push(view);
  select.addEventListener('change', () => renderMixer(view, view.channels));
  slider.addEventListener('pointerdown', () => { view.dragging = true; });
  for (const event of ['pointerup', 'pointercancel', 'lostpointercapture', 'blur'])
    slider.addEventListener(event, () => { view.dragging = false; });
  slider.addEventListener('input', () => { value.textContent = `${slider.value}%`; });
  slider.addEventListener('change', () => sendVolume(view));
  return tile;
}

function renderMixer(view, channels) {
  if (view.dragging || view.sending) return;
  const { select, slider, value } = view;
  const selected = select.value;
  const signature = JSON.stringify(channels.map(channel => [channel.id, channel.name]));
  if (view.signature !== signature) {
    const options = channels.map(channel => new Option(channel.name, channel.id));
    select.replaceChildren(...(options.length ? options : [new Option('오디오 연결 확인', '')]));
    if (channels.some(channel => channel.id === selected)) select.value = selected;
    view.signature = signature;
  }
  view.channels = channels;
  const active = channels.find(channel => channel.id === select.value);
  select.disabled = slider.disabled = !active;
  if (active) slider.value = active.volume;
  value.textContent = active ? `${active.volume}%` : '—';
}

async function refreshMixer() {
  if (!code || !mixerViews.length || readingMixer) return;
  const revision = mixerRevision;
  readingMixer = true;
  try {
    const response = await request('/api/mixer');
    if (response.status === 401) return unpair();
    if (!response.ok) throw new Error('mixer request failed');
    const channels = await response.json();
    if (revision === mixerRevision)
      mixerViews.forEach(view => renderMixer(view, channels));
  } catch {
    if (revision === mixerRevision)
      mixerViews.forEach(view => renderMixer(view, []));
  } finally {
    readingMixer = false;
  }
}

async function sendVolume(view) {
  view.pending = { id: view.select.value, level: view.slider.value };
  if (view.sending) return;
  view.sending = true;
  ++mixerRevision;
  try {
    while (view.pending && view.tile.isConnected) {
      const { id, level } = view.pending;
      view.pending = null;
      const response = await request(`/api/mixer/${encodeURIComponent(id)}/${level}`, 'POST');
      if (response.status === 401) return unpair();
      if (!response.ok) throw new Error('volume request failed');
    }
  } catch {
    showError('볼륨을 바꾸지 못했어요.');
  } finally {
    view.pending = null;
    view.sending = false;
    ++mixerRevision;
    refreshMixer();
  }
}

function updateClocks() {
  if (!clocks.length) return;
  const now = timeFormat.format(new Date());
  clocks.forEach(clock => { if (clock.textContent !== now) clock.textContent = now; });
}

function weatherDescription(code) {
  if (code === 0) return '맑음';
  if (code <= 2) return '구름 조금';
  if (code === 3) return '흐림';
  if (code <= 48) return '안개';
  if (code <= 57) return '이슬비';
  if (code <= 67) return '비';
  if (code <= 77) return '눈';
  if (code <= 82) return '소나기';
  if (code <= 86) return '눈 소나기';
  return '뇌우';
}

async function weatherJson(url) {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 8000);
  try {
    const response = await fetch(url, { signal: controller.signal });
    if (!response.ok) throw new Error('weather request failed');
    return await response.json();
  } finally {
    clearTimeout(timeout);
  }
}

function cityWeather(city) {
  const key = city.toLowerCase();
  const cached = weatherCache.get(key);
  if (cached && Date.now() < cached.expires) return cached.promise;
  const entry = { expires: Date.now() + 15 * 60 * 1000 };
  entry.promise = (async () => {
    const search = new URL('https://geocoding-api.open-meteo.com/v1/search');
    search.search = new URLSearchParams({ name: city, count: '1', language: 'ko' });
    const { results } = await weatherJson(search);
    if (!results?.length) return { error: '도시를 찾을 수 없어요' };
    const { latitude, longitude, name } = results[0];
    const forecast = new URL('https://api.open-meteo.com/v1/forecast');
    forecast.search = new URLSearchParams({ latitude, longitude, current: 'temperature_2m,weather_code' });
    const { current } = await weatherJson(forecast);
    if (!Number.isFinite(current?.temperature_2m) || !Number.isFinite(current?.weather_code))
      throw new Error('weather data is incomplete');
    return { temperature: Math.round(current.temperature_2m), name,
      description: weatherDescription(current.weather_code) };
  })().catch(() => {
    entry.expires = Date.now() + 60 * 1000;
    return { error: '날씨를 불러올 수 없어요' };
  });
  if (weatherCache.size >= 20) weatherCache.delete(weatherCache.keys().next().value);
  weatherCache.set(key, entry);
  return entry.promise;
}

function updateWeatherTiles() {
  weatherTiles.forEach(async ({ tile, value, label, location }) => {
    if (!location) { label.textContent = '도시를 설정하세요'; return; }
    const weather = await cityWeather(location);
    if (!tile.isConnected) return;
    value.textContent = weather.error ? '—' : `${weather.temperature}°C`;
    label.textContent = weather.error || `${weather.name} · ${weather.description}`;
  });
}

async function showAppIcon(slot, layoutVersion, index) {
  try {
    const response = await request(`/api/icon/${layoutVersion}/${index}`);
    if (!response.ok) return;
    const url = URL.createObjectURL(await response.blob());
    const image = new Image(32, 32);
    image.alt = '';
    image.className = 'tile-app-icon';
    image.onload = () => {
      slot.replaceChildren(image);
      URL.revokeObjectURL(url);
    };
    image.onerror = () => URL.revokeObjectURL(url);
    image.src = url;
  } catch { /* Keep the program symbol if its icon is unavailable. */ }
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
  code = codeInput.value.trim();
  noticeUntil = 0;
  setStatus('PC에 연결 중', 'pending');
  refresh();
});

if (code) {
  codeInput.value = code;
  refresh();
}
for (const event of ['dragstart', 'selectstart'])
  deck.addEventListener(event, event => event.preventDefault());

setInterval(() => {
  if (document.hidden) return;
  refresh();
  refreshMixer();
}, 2000);
setInterval(() => { if (!document.hidden) updateClocks(); }, 1000);
setInterval(() => { if (!document.hidden) updateWeatherTiles(); }, 60 * 1000);
document.addEventListener('visibilitychange', () => {
  if (document.hidden) return;
  refresh();
  updateClocks();
  updateWeatherTiles();
  refreshMixer();
});
