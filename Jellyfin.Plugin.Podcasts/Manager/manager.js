(() => {
  'use strict';
  const $ = selector => document.querySelector(selector);
  const base = new URL(location.href);
  base.search = ''; base.hash = ''; base.pathname = base.pathname.replace(/\/?Tilapia\/?$/i, '/');
  const deviceKey = 'tilapia-device-id', tokenKey = 'tilapia-token';
  const deviceId = localStorage.getItem(deviceKey) || crypto.randomUUID();
  localStorage.setItem(deviceKey, deviceId);
  let token = sessionStorage.getItem(tokenKey) || localStorage.getItem(tokenKey) || '';
  let checkedUrl = '', pollTimer, subscriptions = [], searchResults = [];
  const val = (object, camel, pascal) => object?.[camel] ?? object?.[pascal];
  const authHeader = () => `MediaBrowser Client="Tilapia", Device="Web Browser", DeviceId="${deviceId}", Version="1.1.0"${token ? `, Token="${token}"` : ''}`;

  async function request(path, options = {}, json = true) {
    const headers = new Headers(options.headers || {});
    headers.set('Authorization', authHeader());
    if (options.body) headers.set('Content-Type', 'application/json');
    const response = await fetch(new URL(path, base), { ...options, headers });
    if (!response.ok) {
      let message = `Request failed (${response.status}).`;
      try {
        const text = await response.text();
        if (text) {
          try { const body = JSON.parse(text); message = body.error || body.title || message; }
          catch { message = text.length <= 300 ? text : message; }
        }
      } catch { /* response body was unavailable */ }
      throw new Error(message);
    }
    if (!json || response.status === 204) return response;
    return response.json();
  }
  const api = (path, options) => request(path, options, true);
  function status(selector, text, error = false) { const element = $(selector); element.textContent = text || ''; element.classList.toggle('error', error); }
  function saveAuth(result, remember) {
    token = val(result, 'accessToken', 'AccessToken');
    if (!token) throw new Error('Jellyfin did not return a sign-in token.');
    sessionStorage.setItem(tokenKey, token);
    if (remember) localStorage.setItem(tokenKey, token); else localStorage.removeItem(tokenKey);
  }
  function signOut(reload = true) {
    clearInterval(pollTimer); token = ''; sessionStorage.removeItem(tokenKey); localStorage.removeItem(tokenKey);
    if (reload) location.reload();
  }
  async function enter() {
    try {
      const me = await api('Users/Me');
      $('#loginView').classList.add('hidden'); $('#managerView').classList.remove('hidden'); $('#signOut').classList.remove('hidden');
      $('#welcome').textContent = `${val(me, 'name', 'Name')}'s podcasts`; $('#openJellyfin').href = base.href;
      await load();
    } catch { signOut(false); status('#loginStatus', 'Please sign in again.', true); }
  }

  $('#signOut').onclick = () => signOut();
  $('#loginForm').onsubmit = async event => {
    event.preventDefault(); const button = event.submitter; button.disabled = true; status('#loginStatus', 'Signing in...');
    try {
      const result = await api('Users/AuthenticateByName', { method: 'POST', body: JSON.stringify({ Username: $('#username').value, Pw: $('#password').value }) });
      saveAuth(result, $('#remember').checked); $('#password').value = ''; await enter();
    } catch (error) { status('#loginStatus', error.message, true); } finally { button.disabled = false; }
  };
  $('#quickStart').onclick = async event => {
    const button = event.currentTarget; button.disabled = true; status('#loginStatus', 'Requesting a Quick Connect code...');
    try {
      if (!await api('QuickConnect/Enabled')) throw new Error('Quick Connect is disabled on this Jellyfin server. Use username and password below.');
      const quick = await api('QuickConnect/Initiate', { method: 'POST' }); const secret = val(quick, 'secret', 'Secret');
      $('#quickCode strong').textContent = val(quick, 'code', 'Code'); $('#quickCode').classList.remove('hidden'); status('#loginStatus', '');
      pollTimer = setInterval(async () => {
        try {
          const state = await api(`QuickConnect/Connect?secret=${encodeURIComponent(secret)}`);
          if (val(state, 'authenticated', 'Authenticated')) {
            clearInterval(pollTimer); const result = await api('Users/AuthenticateWithQuickConnect', { method: 'POST', body: JSON.stringify({ Secret: secret }) });
            saveAuth(result, true); await enter();
          }
        } catch (error) { clearInterval(pollTimer); status('#loginStatus', error.message, true); }
      }, 2000);
    } catch (error) { button.disabled = false; status('#loginStatus', error.message, true); }
  };

  async function load() {
    status('#libraryStatus', 'Refreshing your podcasts...');
    try { subscriptions = await api('Podcasts/Subscriptions') || []; renderSubscriptions(); renderSearch(); status('#libraryStatus', ''); }
    catch (error) { status('#libraryStatus', error.message, true); }
  }
  $('#refresh').onclick = load;
  $('#libraryFilter').oninput = renderSubscriptions;
  function isAdded(feedUrl) { return subscriptions.some(item => val(val(item, 'subscription', 'Subscription'), 'feedUrl', 'FeedUrl')?.toLowerCase() === feedUrl?.toLowerCase()); }
  function upsertSubscription(item) {
    const id = val(val(item, 'subscription', 'Subscription'), 'id', 'Id');
    subscriptions = subscriptions.filter(existing => val(val(existing, 'subscription', 'Subscription'), 'id', 'Id') !== id);
    subscriptions.push(item);
  }
  function formatChecked(value) {
    if (!value) return '';
    const date = new Date(value); return Number.isNaN(date.valueOf()) ? '' : `Checked ${date.toLocaleString()}`;
  }
  function renderSubscriptions() {
    const filter = $('#libraryFilter').value.trim().toLowerCase(); const list = $('#subscriptions'); list.replaceChildren();
    const visible = subscriptions.filter(item => {
      const sub = val(item, 'subscription', 'Subscription'), feed = val(item, 'feed', 'Feed');
      return !filter || [val(feed, 'title', 'Title'), val(feed, 'description', 'Description'), val(sub, 'feedUrl', 'FeedUrl')].some(x => x?.toLowerCase().includes(filter));
    });
    $('#empty').classList.toggle('hidden', subscriptions.length > 0); $('#count').textContent = `${subscriptions.length} ${subscriptions.length === 1 ? 'podcast' : 'podcasts'}`;
    if (subscriptions.length && !visible.length) { const empty = document.createElement('div'); empty.className = 'empty'; empty.textContent = 'No subscriptions match that filter.'; list.append(empty); }
    for (const item of visible) list.append(subscriptionCard(item));
  }
  function subscriptionCard(item) {
    const sub = val(item, 'subscription', 'Subscription'), feed = val(item, 'feed', 'Feed');
    const id = val(sub, 'id', 'Id'), title = val(feed, 'title', 'Title') || 'Untitled podcast';
    const isPrivate = Boolean(val(sub, 'isPrivate', 'IsPrivate')), available = val(item, 'isAvailable', 'IsAvailable') !== false;
    const card = document.createElement('article'); card.className = 'card';
    const img = document.createElement('img'); img.className = 'cover'; img.alt = ''; const image = val(feed, 'imageUrl', 'ImageUrl'); if (image) img.src = image;
    const info = document.createElement('div'); info.className = 'cardInfo';
    const health = document.createElement('span'); health.className = `health${available ? '' : ' error'}`; health.textContent = available ? (isPrivate ? 'Private - locally cached' : 'Available') : 'Needs attention';
    const heading = document.createElement('h3'); heading.textContent = title; const description = document.createElement('p'); description.textContent = val(feed, 'description', 'Description') || val(sub, 'feedUrl', 'FeedUrl');
    const checked = document.createElement('small'); checked.textContent = formatChecked(val(item, 'lastChecked', 'LastChecked'));
    info.append(health, heading, description, checked);
    if (!available) { const error = document.createElement('p'); error.className = 'cardError'; error.textContent = val(item, 'error', 'Error') || 'This feed could not be refreshed.'; info.append(error); }
    const manage = document.createElement('details'); manage.className = 'manage'; const summary = document.createElement('summary'); summary.textContent = 'Manage';
    manage.ontoggle = () => { if (manage.open) document.querySelectorAll('.manage[open]').forEach(other => { if (other !== manage) other.open = false; }); };
    const actions = document.createElement('div'); actions.className = 'actions';
    const limitLabel = document.createElement('label'); limitLabel.textContent = 'Episodes'; const limit = document.createElement('select');
    limit.innerHTML = isPrivate ? '<option value="1">Latest 1</option><option value="3">Latest 3</option><option value="5">Latest 5</option><option value="10">Latest 10</option>' : '<option value="10">Latest 10</option><option value="50">Latest 50</option><option value="100">Latest 100</option><option value="200">Latest 200</option><option value="500">Latest 500</option><option value="0">All episodes</option>';
    limit.value = String(val(sub, 'episodeLimit', 'EpisodeLimit') ?? 10); limit.onchange = async () => {
      limit.disabled = true; try { await api(`Podcasts/Subscriptions/${id}/EpisodeLimit`, { method: 'PUT', body: JSON.stringify({ EpisodeLimit: Number(limit.value) }) }); status('#libraryStatus', `${title} episode limit saved.`); } catch (error) { status('#libraryStatus', error.message, true); } finally { limit.disabled = false; }
    }; limitLabel.append(limit);
    const modeLabel = document.createElement('label'); modeLabel.textContent = 'Play by'; const mode = document.createElement('select'); mode.innerHTML = '<option value="Stream">Streaming</option><option value="Cache">Caching when played</option>'; mode.value = val(sub, 'mode', 'Mode'); mode.disabled = isPrivate;
    mode.onchange = async () => { mode.disabled = true; try { await api(`Podcasts/Subscriptions/${id}/Mode`, { method: 'PUT', body: JSON.stringify({ Mode: mode.value }) }); status('#libraryStatus', `${title} playback setting saved.`); } catch (error) { status('#libraryStatus', error.message, true); } finally { mode.disabled = isPrivate; } }; modeLabel.append(mode);
    const retry = document.createElement('button'); retry.className = 'quiet'; retry.textContent = 'Retry'; retry.onclick = load;
    const remove = document.createElement('button'); remove.className = 'danger'; remove.textContent = 'Remove'; remove.onclick = async () => {
      if (!confirm(`Remove ${title} from your podcasts?`)) return; remove.disabled = true;
      try { await api(`Podcasts/Subscriptions/${id}`, { method: 'DELETE' }); await load(); } catch (error) { status('#libraryStatus', error.message, true); remove.disabled = false; }
    };
    actions.append(limitLabel, modeLabel); if (!available) actions.append(retry); actions.append(remove); manage.append(summary, actions); card.append(img, info, manage); return card;
  }

  function directoryCountry() { const match = (navigator.language || '').match(/-([A-Za-z]{2})$/); return match ? match[1].toUpperCase() : 'US'; }
  $('#searchForm').onsubmit = async event => {
    event.preventDefault(); const button = event.submitter; button.disabled = true; button.textContent = 'Searching...'; status('#searchStatus', 'Searching public podcasts...');
    try { searchResults = await api(`Podcasts/Directory/Search?q=${encodeURIComponent($('#searchQuery').value.trim())}&country=${directoryCountry()}`) || []; renderSearch(); status('#searchStatus', searchResults.length ? '' : 'No results found. Try another name or use Advanced RSS below.'); }
    catch (error) { status('#searchStatus', error.message, true); } finally { button.disabled = false; button.textContent = 'Search'; }
  };
  function renderSearch() {
    const list = $('#searchResults'); list.replaceChildren(); $('#searchAttribution').classList.toggle('hidden', searchResults.length === 0);
    for (const result of searchResults) {
      const url = val(result, 'feedUrl', 'FeedUrl'), card = document.createElement('article'); card.className = 'searchCard';
      const img = document.createElement('img'); img.className = 'searchCover'; img.alt = ''; const image = val(result, 'imageUrl', 'ImageUrl'); if (image) img.src = image;
      const info = document.createElement('div'); info.className = 'searchInfo'; const title = document.createElement('h3'); title.textContent = val(result, 'title', 'Title'); const publisher = document.createElement('p'); publisher.textContent = val(result, 'publisher', 'Publisher') || 'Publisher unavailable';
      const meta = document.createElement('small'); meta.className = 'searchMeta'; const count = val(result, 'episodeCount', 'EpisodeCount'), genre = val(result, 'genre', 'Genre'); meta.textContent = [genre, count ? `${count} episodes` : ''].filter(Boolean).join(' - '); info.append(title, publisher, meta);
      const buttons = document.createElement('div'); buttons.className = 'resultActions'; const add = document.createElement('button'); add.className = 'primary'; add.textContent = isAdded(url) ? 'Added' : 'Add'; add.disabled = isAdded(url);
      add.onclick = async () => { add.disabled = true; add.textContent = 'Adding...'; try { const item = await api('Podcasts/Subscriptions', { method: 'POST', body: JSON.stringify({ FeedUrl: url, Mode: 'Stream', IsPrivate: false, EpisodeLimit: 10, RetentionWeeks: 4 }) }); upsertSubscription(item); renderSubscriptions(); renderSearch(); status('#libraryStatus', `${val(result, 'title', 'Title')} added.`, false); } catch (error) { add.disabled = false; add.textContent = 'Add'; status('#searchStatus', error.message, true); } };
      const detailsUrl = val(result, 'directoryUrl', 'DirectoryUrl'); if (detailsUrl) { const details = document.createElement('a'); details.className = 'button quiet'; details.href = detailsUrl; details.target = '_blank'; details.rel = 'noopener'; details.textContent = 'Details'; buttons.append(details); }
      buttons.append(add); card.append(img, info, buttons); list.append(card);
    }
  }

  $('#previewForm').onsubmit = async event => {
    event.preventDefault(); const button = event.submitter; button.disabled = true; button.textContent = 'Checking...'; checkedUrl = ''; $('#preview').classList.add('hidden'); status('#status', 'Checking that feed...');
    try {
      const requestedUrl = $('#feedUrl').value.trim(), isPrivate = $('#privateFeed').checked;
      const result = await api('Podcasts/Feeds/Preview', { method: 'POST', body: JSON.stringify({ FeedUrl: requestedUrl, IsPrivate: isPrivate }) }); const feed = val(result, 'feed', 'Feed'), image = val(feed, 'imageUrl', 'ImageUrl'); checkedUrl = requestedUrl;
      const box = $('#preview'); box.querySelector('img').src = image || ''; box.querySelector('img').classList.toggle('hidden', !image); box.querySelector('h3').textContent = val(feed, 'title', 'Title'); box.querySelector('p').textContent = val(feed, 'description', 'Description') || checkedUrl;
      const count = val(result, 'episodeCount', 'EpisodeCount') || 0; box.querySelector('small').textContent = `${count} ${count === 1 ? 'episode' : 'episodes'} available${isPrivate ? ' - private episodes will be cached locally' : ''}`; $('#addMode').value = isPrivate ? 'Cache' : 'Stream'; $('#addMode').disabled = isPrivate; box.classList.remove('hidden'); status('#status', '');
    } catch (error) { status('#status', error.message, true); } finally { button.disabled = false; button.textContent = 'Check feed'; }
  };
  $('#feedUrl').oninput = () => { $('#preview').classList.add('hidden'); checkedUrl = ''; };
  function setPrivateMode(privateFeed) {
    $('#privateFeed').checked = privateFeed; $('#privateOptions').classList.toggle('hidden', !privateFeed);
    $('#addLimit').innerHTML = privateFeed ? '<option value="1">Latest 1</option><option value="3" selected>Latest 3</option><option value="5">Latest 5</option><option value="10">Latest 10</option>' : '<option value="10">Latest 10 episodes</option><option value="50">Latest 50</option><option value="100">Latest 100</option><option value="200">Latest 200</option><option value="500">Latest 500</option><option value="0">All episodes</option>';
    $('#addMode').value = privateFeed ? 'Cache' : 'Stream'; $('#addMode').disabled = privateFeed; $('#preview').classList.add('hidden'); checkedUrl = '';
  }
  $('#privateFeed').onchange = () => setPrivateMode($('#privateFeed').checked);
  $('#addFeed').onclick = async event => {
    if (!checkedUrl) return; const button = event.currentTarget; button.disabled = true; button.textContent = 'Adding...'; status('#status', 'Adding this podcast...');
    try {
      const isPrivate = $('#privateFeed').checked; const item = await api('Podcasts/Subscriptions', { method: 'POST', body: JSON.stringify({ FeedUrl: checkedUrl, Mode: isPrivate ? 'Cache' : $('#addMode').value, IsPrivate: isPrivate, EpisodeLimit: Number($('#addLimit').value), RetentionWeeks: isPrivate ? Number($('#retentionWeeks').value) : 4, SharedWithUserName: isPrivate ? $('#shareUser').value.trim() || null : null }) });
      upsertSubscription(item); renderSubscriptions(); renderSearch(); $('#previewForm').reset(); setPrivateMode(false); status('#status', 'Podcast added. Open Channels > Podcasts in Jellyfin to listen.');
    } catch (error) { status('#status', error.message, true); } finally { button.disabled = false; button.textContent = 'Add to Podcasts'; }
  };

  $('#importOpml').onclick = () => $('#opmlFile').click();
  $('#opmlFile').onchange = async event => {
    const file = event.target.files?.[0]; event.target.value = ''; if (!file) return;
    if (file.size > 2 * 1024 * 1024) { status('#libraryStatus', 'That OPML file is larger than 2 MB.', true); return; }
    $('#importOpml').disabled = true; status('#libraryStatus', 'Importing subscriptions...');
    try { const result = await api('Podcasts/Subscriptions/Opml', { method: 'POST', body: JSON.stringify({ Opml: await file.text() }) }); const issues = val(result, 'issues', 'Issues') || []; const message = `Imported ${val(result, 'added', 'Added')} podcasts; skipped ${val(result, 'skipped', 'Skipped')} duplicates${issues.length ? `; ${issues.length} could not be added` : ''}.`; await load(); status('#libraryStatus', message, issues.length > 0); }
    catch (error) { status('#libraryStatus', error.message, true); } finally { $('#importOpml').disabled = false; }
  };
  $('#exportOpml').onclick = async () => {
    $('#exportOpml').disabled = true; status('#libraryStatus', 'Preparing your OPML file...');
    try { const response = await request('Podcasts/Subscriptions/Opml', {}, false); const blob = await response.blob(); const disposition = response.headers.get('Content-Disposition') || ''; const match = disposition.match(/filename\*?=(?:UTF-8''|\")?([^\";]+)/i); const name = match ? decodeURIComponent(match[1].replace(/\"/g, '')) : 'tilapia-subscriptions.opml'; const url = URL.createObjectURL(blob); const link = document.createElement('a'); link.href = url; link.download = name; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000); status('#libraryStatus', 'OPML export downloaded. Private feed addresses are never included.'); }
    catch (error) { status('#libraryStatus', error.message, true); } finally { $('#exportOpml').disabled = false; }
  };
  if (token) enter();
})();
