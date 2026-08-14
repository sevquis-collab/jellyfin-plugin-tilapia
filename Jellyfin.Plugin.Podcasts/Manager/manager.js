(() => {
  'use strict';
  const $ = s => document.querySelector(s);
  const base = new URL(location.href);
  base.search = ''; base.hash = ''; base.pathname = base.pathname.replace(/\/?Tilapia\/?$/i, '/');
  const deviceKey = 'tilapia-device-id', tokenKey = 'tilapia-token';
  const deviceId = localStorage.getItem(deviceKey) || crypto.randomUUID();
  localStorage.setItem(deviceKey, deviceId);
  let token = sessionStorage.getItem(tokenKey) || localStorage.getItem(tokenKey) || '', checkedUrl = '', pollTimer, subscriptions = [];
  const val = (o, a, b) => o?.[a] ?? o?.[b];
  const authHeader = () => `MediaBrowser Client="Tilapia", Device="Web Browser", DeviceId="${deviceId}", Version="1.0.0"`;

  async function api(path, options = {}) {
    const headers = new Headers(options.headers || {});
    headers.set('X-Emby-Authorization', authHeader());
    if (token) headers.set('X-Emby-Token', token);
    if (options.body) headers.set('Content-Type', 'application/json');
    const response = await fetch(new URL(path, base), { ...options, headers });
    if (!response.ok) {
      let message = `Request failed (${response.status}).`;
      try { const body = await response.json(); message = body.error || body.title || message; } catch { /* no JSON response */ }
      throw new Error(message);
    }
    if (response.status === 204) return null;
    return response.json();
  }

  function setStatus(text, error = false, login = false) {
    const element = $(login ? '#loginStatus' : '#status');
    element.textContent = text || '';
    element.classList.toggle('error', error);
  }

  function saveAuth(result, remember) {
    token = val(result, 'accessToken', 'AccessToken');
    if (!token) throw new Error('Jellyfin did not return a sign-in token.');
    sessionStorage.setItem(tokenKey, token);
    if (remember) localStorage.setItem(tokenKey, token); else localStorage.removeItem(tokenKey);
  }

  async function enter() {
    try {
      const me = await api('Users/Me');
      $('#loginView').classList.add('hidden');
      $('#managerView').classList.remove('hidden');
      $('#signOut').classList.remove('hidden');
      $('#welcome').textContent = `${val(me, 'name', 'Name')}'s podcasts`;
      await load();
    } catch {
      signOut(false);
      setStatus('Please sign in again.', true, true);
    }
  }

  function signOut(reload = true) {
    clearInterval(pollTimer);
    token = '';
    sessionStorage.removeItem(tokenKey);
    localStorage.removeItem(tokenKey);
    if (reload) location.reload();
  }

  $('#signOut').onclick = () => signOut();
  $('#loginForm').onsubmit = async event => {
    event.preventDefault();
    const button = event.submitter;
    button.disabled = true;
    setStatus('Signing in', false, true);
    try {
      const result = await api('Users/AuthenticateByName', { method: 'POST', body: JSON.stringify({ Username: $('#username').value, Pw: $('#password').value }) });
      saveAuth(result, $('#remember').checked);
      $('#password').value = '';
      await enter();
    } catch (error) { setStatus(error.message, true, true); }
    finally { button.disabled = false; }
  };

  $('#quickStart').onclick = async event => {
    const button = event.currentTarget;
    button.disabled = true;
    setStatus('Requesting a Quick Connect code', false, true);
    try {
      if (!await api('QuickConnect/Enabled')) throw new Error('Quick Connect is disabled on this Jellyfin server. Use username and password below.');
      const request = await api('QuickConnect/Initiate', { method: 'POST' });
      const secret = val(request, 'secret', 'Secret');
      $('#quickCode strong').textContent = val(request, 'code', 'Code');
      $('#quickCode').classList.remove('hidden');
      setStatus('', false, true);
      pollTimer = setInterval(async () => {
        try {
          const state = await api(`QuickConnect/Connect?secret=${encodeURIComponent(secret)}`);
          if (val(state, 'authenticated', 'Authenticated')) {
            clearInterval(pollTimer);
            const result = await api('Users/AuthenticateWithQuickConnect', { method: 'POST', body: JSON.stringify({ Secret: secret }) });
            saveAuth(result, true);
            await enter();
          }
        } catch (error) { clearInterval(pollTimer); setStatus(error.message, true, true); }
      }, 2000);
    } catch (error) { button.disabled = false; setStatus(error.message, true, true); }
  };

  async function load() {
    setStatus('Refreshing your podcasts…');
    try { subscriptions = await api('Podcasts/Subscriptions') || []; render(subscriptions); setStatus(''); }
    catch (error) { setStatus(error.message, true); }
  }
  $('#refresh').onclick = load;

  function render(items) {
    const list = $('#subscriptions');
    list.replaceChildren();
    $('#empty').classList.toggle('hidden', items.length > 0);
    $('#count').textContent = `${items.length} ${items.length === 1 ? 'podcast' : 'podcasts'}`;
    for (const item of items) {
      const sub = val(item, 'subscription', 'Subscription'), feed = val(item, 'feed', 'Feed');
      const id = val(sub, 'id', 'Id'), title = val(feed, 'title', 'Title') || 'Untitled podcast';
      const isPrivate = Boolean(val(sub, 'isPrivate', 'IsPrivate'));
      const card = document.createElement('article'); card.className = 'card';
      const img = document.createElement('img'); img.className = 'cover'; img.alt = '';
      const image = val(feed, 'imageUrl', 'ImageUrl'); if (image) img.src = image;
      const info = document.createElement('div'), heading = document.createElement('h3'), description = document.createElement('p');
      heading.textContent = title;
      description.textContent = val(feed, 'description', 'Description') || val(sub, 'feedUrl', 'FeedUrl');
      if (isPrivate) { const badge = document.createElement('span'); badge.className = 'badge'; badge.textContent = 'Private · locally cached'; info.append(badge); }
      info.append(heading, description);
      const manage = document.createElement('details'); manage.className = 'manage';
      const manageSummary = document.createElement('summary'); manageSummary.textContent = 'Manage';
      manage.ontoggle = () => {
        if (manage.open) document.querySelectorAll('.manage[open]').forEach(other => { if (other !== manage) other.open = false; });
      };
      const actions = document.createElement('div'); actions.className = 'actions';
      const label = document.createElement('label'); label.textContent = 'Play by';
      const select = document.createElement('select');
      select.innerHTML = '<option value="Stream">Streaming</option><option value="Cache">Caching when played</option>';
      select.value = val(sub, 'mode', 'Mode');
      select.disabled = isPrivate;
      select.onchange = async () => {
        select.disabled = true;
        try { await api(`Podcasts/Subscriptions/${id}/Mode`, { method: 'PUT', body: JSON.stringify({ Mode: select.value }) }); setStatus(`${title} playback setting saved.`); }
        catch (error) { setStatus(error.message, true); }
        finally { select.disabled = false; }
      };
      label.append(select);
      const limitLabel = document.createElement('label'); limitLabel.textContent = 'Episodes';
      const limitSelect = document.createElement('select');
      limitSelect.innerHTML = isPrivate
        ? '<option value="1">Latest 1</option><option value="3">Latest 3</option><option value="5">Latest 5</option><option value="10">Latest 10</option>'
        : '<option value="10">Latest 10</option><option value="50">Latest 50</option><option value="100">Latest 100</option><option value="200">Latest 200</option><option value="500">Latest 500</option><option value="0">All episodes</option>';
      limitSelect.value = String(val(sub, 'episodeLimit', 'EpisodeLimit') ?? 10);
      limitSelect.onchange = async () => {
        limitSelect.disabled = true;
        try { await api(`Podcasts/Subscriptions/${id}/EpisodeLimit`, { method: 'PUT', body: JSON.stringify({ EpisodeLimit: Number(limitSelect.value) }) }); setStatus(`${title} episode limit saved. Refresh the Podcasts channel to see it.`); }
        catch (error) { setStatus(error.message, true); }
        finally { limitSelect.disabled = false; }
      };
      limitLabel.append(limitSelect);
      const remove = document.createElement('button'); remove.className = 'danger'; remove.textContent = 'Remove';
      remove.onclick = async () => {
        if (!confirm(`Remove ${title} from your podcasts?`)) return;
        remove.disabled = true;
        try { await api(`Podcasts/Subscriptions/${id}`, { method: 'DELETE' }); await load(); }
        catch (error) { setStatus(error.message, true); remove.disabled = false; }
      };
      actions.append(limitLabel, label, remove); manage.append(manageSummary, actions); card.append(img, info, manage); list.append(card);
    }
  }

  $('#previewForm').onsubmit = async event => {
    event.preventDefault();
    const button = event.submitter; button.disabled = true; button.textContent = 'Looking…'; checkedUrl = ''; $('#preview').classList.add('hidden'); setStatus('Checking that feed…');
    try {
      const requestedUrl = $('#feedUrl').value.trim();
      const isPrivate = $('#privateFeed').checked;
      const result = await api('Podcasts/Feeds/Preview', { method: 'POST', body: JSON.stringify({ FeedUrl: requestedUrl, IsPrivate: isPrivate }) });
      const feed = val(result, 'feed', 'Feed'), box = $('#preview'), image = val(feed, 'imageUrl', 'ImageUrl');
      checkedUrl = requestedUrl;
      box.querySelector('img').src = image || ''; box.querySelector('img').classList.toggle('hidden', !image);
      box.querySelector('h3').textContent = val(feed, 'title', 'Title');
      box.querySelector('p').textContent = val(feed, 'description', 'Description') || checkedUrl;
      const episodeCount = val(result, 'episodeCount', 'EpisodeCount') || 0;
      box.querySelector('small').textContent = `${episodeCount} ${episodeCount === 1 ? 'episode' : 'episodes'} available${isPrivate ? ' · private episodes will be cached locally' : ''}`;
      $('#addMode').value = isPrivate ? 'Cache' : 'Stream';
      $('#addMode').disabled = isPrivate;
      box.classList.remove('hidden'); setStatus('');
    } catch (error) { setStatus(error.message, true); }
    finally { button.disabled = false; button.textContent = 'Find podcast'; }
  };
  $('#feedUrl').oninput = () => { $('#preview').classList.add('hidden'); checkedUrl = ''; };
  function setPrivateMode(privateFeed) {
    $('#privateFeed').checked = privateFeed;
    $('#privateOptions').classList.toggle('hidden', !privateFeed);
    $('#addLimit').innerHTML = privateFeed ? '<option value="1">Latest 1</option><option value="3" selected>Latest 3</option><option value="5">Latest 5</option><option value="10">Latest 10</option>' : '<option value="10">Latest 10 episodes</option><option value="50">Latest 50</option><option value="100">Latest 100</option><option value="200">Latest 200</option><option value="500">Latest 500</option><option value="0">All episodes</option>';
    $('#addMode').value = privateFeed ? 'Cache' : 'Stream'; $('#addMode').disabled = privateFeed;
    $('#preview').classList.add('hidden'); checkedUrl = '';
  }
  $('#privateFeed').onchange = () => setPrivateMode($('#privateFeed').checked);
  $('#addFeed').onclick = async event => {
    if (!checkedUrl) return;
    const button = event.currentTarget;
    button.disabled = true; button.textContent = 'Adding…'; setStatus('Adding this podcast…');
    try {
      const isPrivate = $('#privateFeed').checked;
      const added = await api('Podcasts/Subscriptions', { method: 'POST', body: JSON.stringify({ FeedUrl: checkedUrl, Mode: isPrivate ? 'Cache' : $('#addMode').value, IsPrivate: isPrivate, EpisodeLimit: Number($('#addLimit').value), RetentionWeeks: isPrivate ? Number($('#retentionWeeks').value) : 4, SharedWithUserName: isPrivate ? $('#shareUser').value.trim() || null : null }) });
      const addedSubscription = val(added, 'subscription', 'Subscription');
      const addedId = val(addedSubscription, 'id', 'Id');
      subscriptions = subscriptions.filter(item => val(val(item, 'subscription', 'Subscription'), 'id', 'Id') !== addedId);
      subscriptions.push(added);
      render(subscriptions);
      $('#previewForm').reset(); setPrivateMode(false);
      setStatus('Podcast added. Open Channels → Podcasts in Jellyfin to listen.');
    } catch (error) { setStatus(error.message, true); }
    finally { button.disabled = false; button.textContent = 'Add to Podcasts'; }
  };
  if (token) enter();
})();
