import { icon } from './icons.js';
import { configureResumes, renderResumes, openTailoring } from './resumes.js';

const $ = (selector, root = document) => root.querySelector(selector);
const trackerFilters = { priority: '', availability: '', city: '' };
const statusLabel = status => ({ NotApplied: 'Not applied', Assessment: 'OA / Assessment', Interview: 'Interviewing' }[status] || status);
const statuses = ['NotApplied', 'Saved', 'Applied', 'Assessment', 'Interview', 'Offer', 'Rejected', 'Withdrawn'];
const views = { jobs: 'Jobs', companies: 'Companies', applications: 'Applications', reminders: 'Reminders', resumes: 'Resumes' };
const escape = value => String(value ?? '').replace(/[&<>"']/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[character]);
const readPreference = key => { try { return localStorage.getItem(key); } catch { return null; } };
const savePreference = (key, value) => { try { localStorage.setItem(key, value); } catch { /* The workspace also works without local storage. */ } };
const state = {
  view: Object.hasOwn(views, location.hash.slice(1)) ? location.hash.slice(1) : 'jobs',
  userId: Number(readPreference('lutzpath.profile')) || null,
  users: [], jobs: [], applications: [], reminders: [],
  query: '', applicationTab: 'All', status: '', reminderTab: 'Upcoming',
  sheetSync: null, errors: [], loading: true
};
let loadVersion = 0;
configureResumes({ userId: () => state.userId });
let editorState = null;
let saving = false;
let busyAction = false;

const currentUser = () => state.users.find(user => user.id === state.userId);
const jobFor = application => state.jobs.find(job => job.id === application?.jobId);
const appFor = reminder => state.applications.find(application => application.id === reminder.applicationId);
const activeApplications = () => state.applications.filter(application => ['Applied', 'Assessment', 'Interview'].includes(application.status));
const initials = name => String(name || 'LP').trim().split(/\s+/).slice(0, 2).map(part => part[0]).join('').toUpperCase();
const formatDate = (value, options = {}) => value ? new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', ...options }).format(new Date(value)) : '—';
const formatTime = value => new Intl.DateTimeFormat(undefined, { hour: 'numeric', minute: '2-digit' }).format(new Date(value));
const todayDate = () => toDateInput(new Date());
const toDateInput = value => {
  if (!value) return '';
  const date = new Date(value);
  return [date.getFullYear(), String(date.getMonth() + 1).padStart(2, '0'), String(date.getDate()).padStart(2, '0')].join('-');
};
const toLocalInput = value => value ? toDateInput(value) + 'T' + String(new Date(value).getHours()).padStart(2, '0') + ':' + String(new Date(value).getMinutes()).padStart(2, '0') : '';
const dateToUtc = value => value ? new Date(value + 'T12:00:00').toISOString() : null;
const localToUtc = value => value ? new Date(value).toISOString() : null;
const safeUrl = value => {
  try { const url = new URL(value); return ['http:', 'https:'].includes(url.protocol) ? url.href : null; } catch { return null; }
};
const companyAvatar = company => {
  const palette = [['#eef2e8', '#788a61'], ['#eef2f8', '#7f92ad'], ['#f7eee7', '#b39173'], ['#f1edf7', '#9d8bb1'], ['#edf4ef', '#7f9d88']];
  const index = [...(company || '')].reduce((sum, char) => sum + char.charCodeAt(0), 0) % palette.length;
  const [background, color] = palette[index];
  return `<span class="company-avatar" style="background:${background};color:${color}" aria-hidden="true">${escape(initials(company))}</span>`;
};
const badge = status => `<span class="status-badge status-${escape(status.toLowerCase())}">${escape(statusLabel(status))}</span>`;
const actionButton = (label, action, symbol = 'plus', primary = false, extra = '') => `<button class="button${primary ? ' primary' : ''}" data-action="${action}" ${extra}>${icon(symbol)}${escape(label)}</button>`;
const empty = (symbol, title, message, button = '', compact = false) => `<div class="empty-state${compact ? ' compact' : ''}"><span class="empty-icon">${icon(symbol)}</span><h3>${escape(title)}</h3><p>${escape(message)}</p>${button}</div>`;
const dueLabel = reminder => {
  const due = new Date(reminder.dueDate);
  if (due < new Date() && !reminder.completed) return 'Overdue';
  const days = Math.round((new Date(toDateInput(due) + 'T12:00:00') - new Date(todayDate() + 'T12:00:00')) / 86400000);
  return days === 0 ? 'Today' : days === 1 ? 'Tomorrow' : formatDate(due);
};

async function api(path, options = {}) {
  let response;
  try {
    response = await fetch('/api' + path, {
      ...options,
      headers: { ...(options.body ? { 'Content-Type': 'application/json' } : {}), ...options.headers },
      signal: options.signal || AbortSignal.timeout(20000)
    });
  } catch (error) {
    if (error.name === 'TimeoutError' || error.name === 'AbortError') throw new Error('This is taking longer than usual. Please try again.');
    throw new Error('We couldn’t connect. Check that LutzPath is running, then try again.');
  }
  if (!response.ok) {
    let problem = null;
    try { problem = await response.json(); } catch { /* A proxy outage may return HTML instead of JSON. */ }
    const validation = problem?.errors ? Object.values(problem.errors).flat().join(' ') : null;
    throw new Error(validation || problem?.detail || (response.status >= 500
      ? 'Part of your workspace is unavailable. Please try again in a moment.'
      : problem?.title || 'We couldn’t save that change. Please try again.'));
  }
  return response.status === 204 ? null : response.json();
}

async function loadWorkspace({ welcome = false } = {}) {
  const version = ++loadVersion;
  $('#refresh-button').classList.add('spinning');
  const errors = [];
  const [users, jobs, sheetSync] = await Promise.allSettled([api('/users'), api('/jobs'), api('/integrations/google-sheets/status')]);
  if (sheetSync.status === 'fulfilled') state.sheetSync = sheetSync.value;
  if (version !== loadVersion) return;
  if (users.status === 'fulfilled') {
    const previousUserId = state.userId;
    state.users = users.value;
    if (!state.users.some(user => user.id === state.userId)) state.userId = state.users[0]?.id || null;
    if (previousUserId !== state.userId) {
      state.applications = [];
      state.reminders = [];
    }
    if (state.userId) savePreference('lutzpath.profile', state.userId);
  } else errors.push('Profiles could not be loaded.');
  if (jobs.status === 'fulfilled') state.jobs = jobs.value;
  else errors.push('Job details could not be loaded.');

  if (state.userId) {
    const [applications, reminders] = await Promise.allSettled([
      api('/applications/user/' + state.userId), api('/reminders/user/' + state.userId)
    ]);
    if (version !== loadVersion) return;
    if (applications.status === 'fulfilled') state.applications = applications.value;
    else errors.push('Applications could not be refreshed.');
    if (reminders.status === 'fulfilled') state.reminders = reminders.value;
    else errors.push('Reminders could not be refreshed.');
  } else {
    state.applications = [];
    state.reminders = [];
  }
  state.errors = errors;
  state.loading = false;
  $('#refresh-button').classList.remove('spinning');
  render();
  if (pendingScrollRestore !== null) { const top = pendingScrollRestore; pendingScrollRestore = null; requestAnimationFrame(() => window.scrollTo({top, behavior:'instant'})); }
  if (welcome && users.status === 'fulfilled' && !state.users.length && !$('#editor').open) openProfile();
}

function renderChrome() {
  const counts = { applications: state.applications.filter(a => !['Saved','NotApplied'].includes(a.status)).length, jobs: new Set(state.applications.map(application => application.jobId)).size, reminders: state.reminders.filter(reminder => !reminder.completed).length };
  $('#navigation').innerHTML = Object.entries(views).map(([key, label]) => `<a href="#${key}" class="${(state.view === key || state.view === 'company' && key === 'companies') ? 'active' : ''}" ${state.view === key ? 'aria-current="page"' : ''}>${icon(({ companies: 'grid', applications: 'briefcase', jobs: 'bookmark', reminders: 'bell' })[key])}<span>${label}</span>${counts[key] ? `<span class="nav-count">${counts[key]}</span>` : ''}</a>`).join('');
  $('#breadcrumb-current').textContent = views[state.view] || (state.view === 'company' ? 'Company jobs' : 'Job details');
  const user = currentUser();
  const fullName = user ? user.firstName + ' ' + user.lastName : 'Your profile';
  $('#sidebar-name').textContent = fullName;
  $('#sidebar-avatar').textContent = user ? initials(fullName) : 'LP';
  const profile = $('#profile-select');
  profile.innerHTML = state.users.length ? state.users.map(user => `<option value="${user.id}">${escape(user.firstName + ' ' + user.lastName)}</option>`).join('') : '<option value="">Your profile</option>';
  profile.value = state.userId ? String(state.userId) : '';
  $('#reminders-button').innerHTML = icon('bell') + (state.reminders.some(reminder => !reminder.completed) ? '<span class="notification-dot"></span>' : '');
  const banner = $('#connection-banner');
  banner.hidden = !state.errors.length;
  banner.innerHTML = state.errors.length ? icon('info') + `<span>${escape(state.errors.join(' '))} Please try again in a moment.</span><button data-action="refresh">Try again</button>` : '';
}

function render() {
  renderChrome();
  const focused = document.activeElement;
  const restoreSearch = focused?.id === 'workspace-search' ? { start: focused.selectionStart, end: focused.selectionEnd } : null;
  const main = $('#main');
  main.setAttribute('aria-busy', String(state.loading));
  if (state.loading) return;
  main.innerHTML = ({ jobs: renderWorkspaceJobs, companies: renderCompanyDirectory, company: renderCompanyJobs, job: renderJobDetail, applications: renderSubmittedApplications, reminders: renderReminders, resumes: renderResumes })[state.view]();
  if (restoreSearch) {
    const search = $('#workspace-search');
    search?.focus({ preventScroll: true });
    search?.setSelectionRange(restoreSearch.start, restoreSearch.end);
  }
}

function pageHeading(eyebrow, title, description, actions) {
  return `<section class="page-heading"><div><div class="eyebrow"><span class="live-dot"></span>${escape(eyebrow)}</div><h1>${title}</h1><p class="page-description">${escape(description)}</p></div><div class="heading-actions">${actions}</div></section>`;
}

function searchControl(placeholder) {
  return `<label class="search-box">${icon('search')}<span class="sr-only">Search ${escape(placeholder)}</span><input type="search" id="workspace-search" placeholder="${escape(placeholder)}" value="${escape(state.query)}" autocomplete="off"></label>`;
}

function renderReminders() {
  const query = state.query.toLowerCase();
  const reminders = state.reminders.filter(reminder => (state.reminderTab === 'All' || (state.reminderTab === 'Completed' ? reminder.completed : !reminder.completed)) &&
    (!query || [reminder.title, jobFor(appFor(reminder))?.company, jobFor(appFor(reminder))?.title].some(value => value?.toLowerCase().includes(query)))).sort((a, b) => new Date(a.dueDate) - new Date(b.dueDate));
  const groups = [
    ['Overdue', reminders.filter(reminder => !reminder.completed && new Date(reminder.dueDate) < new Date())],
    ['Today', reminders.filter(reminder => !reminder.completed && new Date(reminder.dueDate) >= new Date() && toDateInput(reminder.dueDate) === todayDate())],
    ['Up next', reminders.filter(reminder => !reminder.completed && toDateInput(reminder.dueDate) !== todayDate() && new Date(reminder.dueDate) >= new Date())],
    ['Completed', reminders.filter(reminder => reminder.completed)]
  ];
  return pageHeading('Your next steps', 'A little reminder.<br><span class="soft">A lot less to remember.</span>', 'Follow-ups, interviews, and deadlines. Give them a place.', actionButton('Add reminder', 'new-reminder', 'plus', true)) +
    `<section class="panel"><div class="toolbar"><div class="tabs" role="group" aria-label="Reminder filter">${['Upcoming', 'Completed', 'All'].map(tab => `<button class="tab ${state.reminderTab === tab ? 'active' : ''}" data-reminder-tab="${tab}" aria-pressed="${state.reminderTab === tab}">${tab}</button>`).join('')}</div><div class="filter-controls">${searchControl('Search reminders…')}</div></div>${reminders.length ? groups.filter(([, entries]) => entries.length).map(([name, entries]) => `<section><h2 class="reminder-group-title ${name === 'Overdue' ? 'overdue' : ''}">${name}</h2>${entries.map(reminderRow).join('')}</section>`).join('') : empty('calendar', state.query ? 'No reminders match' : state.reminderTab === 'Completed' ? 'Good things take small steps' : 'A little breathing room', state.query ? 'Try another title, company, or role.' : state.reminderTab === 'Completed' ? 'Your completed reminders will appear here.' : 'Add a reminder so the important things stay in view.', actionButton('Add reminder', 'new-reminder', 'plus', true))}<div class="panel-bottom"><span>${state.reminders.filter(reminder => !reminder.completed).length} upcoming · ${state.reminders.filter(reminder => reminder.completed).length} completed</span><span>Times shown in your local time.</span></div></section>`;
}

function reminderRow(reminder) {
  const job = jobFor(appFor(reminder));
  return `<article class="reminder-row ${reminder.completed ? 'completed' : ''}" data-reminder-id="${reminder.id}"><button class="check-button ${reminder.completed ? 'checked' : ''}" data-action="toggle-reminder" data-id="${reminder.id}" aria-label="${reminder.completed ? 'Mark incomplete' : 'Complete'}: ${escape(reminder.title)}" aria-pressed="${reminder.completed}">${reminder.completed ? icon('check') : ''}</button><div class="reminder-row-main"><button class="reminder-title" data-action="edit-reminder" data-id="${reminder.id}">${escape(reminder.title)}</button><p class="reminder-subtitle">${escape(job ? job.company + ' · ' + job.title : 'Application #' + reminder.applicationId)}</p></div><div class="reminder-due ${dueLabel(reminder) === 'Overdue' ? 'overdue' : ''}">${escape(dueLabel(reminder))}<small>${formatDate(reminder.dueDate)} · ${formatTime(reminder.dueDate)}</small></div><div class="reminder-row-actions"><button class="icon-button" data-action="edit-reminder" data-id="${reminder.id}" aria-label="Edit reminder: ${escape(reminder.title)}">${icon('edit')}</button><button class="icon-button danger" data-action="delete-reminder" data-id="${reminder.id}" aria-label="Delete reminder: ${escape(reminder.title)}">${icon('trash')}</button></div></article>`;
}
function field(name, label, type = 'text', value = '', attributes = '', optional = false) {
  return `<div class="field"><label for="field-${name}">${escape(label)}${optional ? '<span class="optional">optional</span>' : ''}</label><input id="field-${name}" name="${name}" type="${type}" value="${escape(value)}" ${attributes}></div>`;
}

function selectField(name, label, options, value) {
  return `<div class="field"><label for="field-${name}">${escape(label)}</label><select id="field-${name}" name="${name}" required>${options.map(([id, text]) => `<option value="${escape(id)}" ${String(id) === String(value) ? 'selected' : ''}>${escape(text)}</option>`).join('')}</select></div>`;
}

function textareaField(name, label, value = '', placeholder = '') {
  return `<div class="field"><label for="field-${name}">${escape(label)}<span class="optional">optional</span></label><textarea id="field-${name}" name="${name}" maxlength="${['notes', 'description'].includes(name) ? 10000 : 20000}" placeholder="${escape(placeholder)}">${escape(value)}</textarea></div>`;
}

function showEditor(config, title, description, fields, submitLabel, footer = '', subheader = '') {
  const dialog = $('#editor');
  if (dialog.open) dialog.close();
  editorState = config;
  dialog.innerHTML = `<div class="dialog-heading"><div><h2 id="editor-title">${escape(title)}</h2><p>${escape(description)}</p></div><button type="button" class="icon-button" data-action="close-editor" aria-label="Close dialog">${icon('close')}</button></div>${subheader}<form id="editor-form"><div class="form-body">${fields}<div class="form-error" id="form-error" role="alert" hidden></div></div><div class="dialog-footer">${footer}<button type="button" class="button" data-action="close-editor">Cancel</button><button type="submit" class="button primary" id="submit-editor">${icon('check')}${escape(submitLabel)}</button></div></form>`;
  dialog.showModal();
  requestAnimationFrame(() => $('input, select, textarea', dialog)?.focus());
}

function ensureProfile() {
  if (currentUser()) return true;
  openProfile();
  return false;
}

function openProfile(edit = false) {
  const user = edit ? currentUser() : null;
  const fields = `<div class="field-row">${field('firstName', 'First name', 'text', user?.firstName, 'required maxlength="100" autocomplete="given-name"') }${field('lastName', 'Last name', 'text', user?.lastName, 'required maxlength="100" autocomplete="family-name"')}</div>${field('email', 'Email address', 'email', user?.email, 'required maxlength="254" autocomplete="email"')}${field('graduationDate', 'Graduation date', 'date', user?.graduationDate, '', true)}<p class="form-hint">Your profile keeps your applications and reminders together. You can switch between profiles at the top of your workspace.</p>`;
  showEditor({ type: 'profile', id: user?.id }, user ? 'A little about you' : 'Make yourself at home', user ? 'Keep your profile up to date.' : 'Start with a profile. Your next chapter begins here.', fields, user ? 'Save changes' : 'Create profile');
}

function openJob(id = null, thenApply = false) {
  if (!ensureProfile()) return;
  const job = state.jobs.find(job => job.id === id);
  const fields = `${field('company', 'Company', 'text', job?.company, 'required maxlength="200" placeholder="e.g. Microsoft"')}${field('title', 'Job title', 'text', job?.title, 'required maxlength="200" placeholder="e.g. Graduate software developer"')}<div class="field-row">${field('location', 'Location', 'text', job?.location, 'maxlength="200" placeholder="e.g. Chicago or remote"', true)}${field('datePosted', 'Date posted', 'date', job?.datePosted, '', true)}</div>${field('jobUrl', 'Job posting link', 'url', job?.jobUrl, 'maxlength="2048" placeholder="https://…"', true)}${textareaField('description', 'A few details', job?.description, 'What caught your eye?')}<details class="tracker-details"><summary>Visa information & posting availability</summary>${textareaField('optFriendlyInformation','OPT friendly information',job?.optFriendlyInformation)}${textareaField('h1bVisaInformation','H-1B visa information',job?.h1bVisaInformation)}${textareaField('likelyInterviewProcess','Likely interview process',job?.likelyInterviewProcess)}${textareaField('availabilityCheckNotes','Availability check evidence',job?.availabilityCheckNotes)}${selectField('postingAvailability','Posting availability',[['Unknown','Unknown / recheck'],['Open','Open'],['Closed','Closed']],job?.postingAvailability || 'Unknown')}${field('lastAvailabilityCheck','Last availability check','datetime-local',toLocalInput(job?.lastAvailabilityCheck),'',true)}<p class="field-hint">Keep source qualifications and evidence. Availability is recorded manually.</p></details>${job ? '<p class="form-hint">Changes update this shared job posting for any profiles using it.</p>' : '<p class="form-hint">We’ll add this to your saved jobs so you can come back when you’re ready.</p>'}`;
  showEditor({ type: 'job', id: job?.id, thenApply }, job ? 'The opportunity, in detail' : 'Save a possibility', job ? 'Keep the details that matter.' : 'A good opportunity deserves a place.', fields, job ? 'Save changes' : thenApply ? 'Save & continue' : 'Save job');
}

function openApplication(id = null) {
  if (!ensureProfile()) return;
  if (!state.jobs.length) {
    openJob(null, true);
    return;
  }
  const application = state.applications.find(application => application.id === id);
  const job = jobFor(application);
  const options = state.jobs.map(job => [job.id, job.title + ' — ' + job.company]);
  const fields = (application ? '<p class="form-hint"><strong>' + escape(job?.title || 'Job unavailable') + '</strong><br>' + escape(job?.company || '') + '</p>' : selectField('jobId', 'Opportunity', options, state.jobs.at(-1)?.id)) +
    `<div class="field-row">${selectField('status', 'Application status', statuses.map(status => [status, statusLabel(status)]), application?.status || 'Applied')}${field('dateApplied', 'Applied on', 'date', application ? toDateInput(application.dateApplied) : todayDate(), '', true)}</div>${field('interviewDate', 'Interview date & time', 'datetime-local', toLocalInput(application?.interviewDate), '', true)}<p class="field-hint">Times are entered in your local time and saved automatically in UTC.</p>${textareaField('notes', 'Your notes', application?.notes, 'People, next steps, questions… keep it all here.')}<details class="tracker-details"><summary>Tracking & preparation notes</summary>${field('dateFound','Date found','date',application ? application.dateFound : todayDate(),'',true)}${field('priority','Priority','text',application?.priority,'maxlength="20000" placeholder="e.g. High or High–Medium"',true)}${textareaField('interviewPrepNotes','Interview prep notes',application?.interviewPrepNotes)}${textareaField('resumeTips','Resume tips',application?.resumeTips)}${textareaField('dsaTopicsToKnow','DSA topics to know',application?.dsaTopicsToKnow)}${textareaField('additionalSourceNotes','Additional source notes',application?.additionalSourceNotes)}</details><p class="form-hint">Manage follow-up dates in Reminders after saving this application. Link each reminder to the relevant application.</p>`;
  const footer = application ? `<button type="button" class="icon-button danger delete-action" data-action="delete-application" data-id="${application.id}" aria-label="Delete application">${icon('trash')}</button>` : '';
  const subheader = job ? `<div class="dialog-subheader">${companyAvatar(job.company)}<span><strong>${escape(job.company)}</strong><small>${escape(job.location || 'Your next opportunity')}</small></span></div>` : '';
  showEditor({ type: 'application', id: application?.id }, application ? 'Keep your next step in view' : 'A new opportunity', application ? 'Status, interview details, and everything in between.' : 'A little organization for a big possibility.', fields, application ? 'Save changes' : 'Add application', footer, subheader);
}

function openReminder(id = null, applicationId = null) {
  if (!ensureProfile()) return;
  if (!state.applications.length) {
    toast('Add an application or save a job before creating a reminder.');
    openApplication();
    return;
  }
  const reminder = state.reminders.find(reminder => reminder.id === id);
  const options = state.applications.map(application => {
    const job = jobFor(application);
    return [application.id, job ? job.title + ' — ' + job.company : 'Application #' + application.id];
  });
  // Include a deleted application's reference for an existing reminder so its state stays visible.
  if (reminder && !options.some(([id]) => id === reminder.applicationId)) options.push([reminder.applicationId, 'Application #' + reminder.applicationId + ' (no longer available)']);
  const tomorrow = new Date();
  tomorrow.setDate(tomorrow.getDate() + 1);
  tomorrow.setHours(9, 0, 0, 0);
  const fields = field('title', 'What would you like to remember?', 'text', reminder?.title, 'required maxlength="200" placeholder="e.g. Follow up with the recruiter"') +
    selectField('applicationId', 'Related application', options, reminder?.applicationId || applicationId || options[0][0]) +
    field('dueDate', 'When', 'datetime-local', toLocalInput(reminder?.dueDate || tomorrow), 'required') +
    '<p class="field-hint">Shown in your local time. Your reminder stays here until you complete it.</p>' +
    (reminder ? `<label class="check-label"><input type="checkbox" name="completed" ${reminder.completed ? 'checked' : ''}>Mark as completed</label>` : '');
  const footer = reminder ? `<button type="button" class="icon-button danger delete-action" data-action="delete-reminder" data-id="${reminder.id}" aria-label="Delete reminder">${icon('trash')}</button>` : '';
  showEditor({ type: 'reminder', id: reminder?.id }, reminder ? 'A moment for your next step' : 'One less thing to remember', 'Give your follow-up, deadline, or interview a little nudge.', fields, reminder ? 'Save changes' : 'Add reminder', footer);
}

async function saveEditor(event) {
  event.preventDefault();
  if (saving || !editorState) return;
  const form = event.target;
  if (!form.reportValidity()) return;
  saving = true;
  const config = editorState;
  const data = new FormData(form);
  const value = name => String(data.get(name) || '').trim();
  const optional = name => value(name) || null;
  const error = $('#form-error');
  error.hidden = true;
  form.querySelectorAll('button').forEach(button => { button.disabled = true; });
  $('#submit-editor').innerHTML = '<span class="spinner"></span> Saving…';
  let message = '';
  let continueApplication = null;
  try {
    if (config.type === 'profile') {
      const body = { firstName: value('firstName'), lastName: value('lastName'), email: value('email'), graduationDate: optional('graduationDate') };
      const user = await api('/users' + (config.id ? '/' + config.id : ''), { method: config.id ? 'PUT' : 'POST', body: JSON.stringify(body) });
      if (config.id) state.users = state.users.map(item => item.id === user.id ? user : item);
      else state.users.push(user);
      if (state.userId !== user.id) { state.applications = []; state.reminders = []; }
      state.userId = user.id;
      savePreference('lutzpath.profile', user.id);
      message = config.id ? 'Profile updated. Looking good.' : 'Welcome, ' + user.firstName + '. Make yourself at home.';
    } else if (config.type === 'job') {
      const jobUrl = optional('jobUrl');
      if (jobUrl && !safeUrl(jobUrl)) throw new Error('Please use a job link beginning with http:// or https://.');
      const body = { company: value('company'), title: value('title'), location: optional('location'), datePosted: optional('datePosted'), jobUrl, description: optional('description'), optFriendlyInformation: optional('optFriendlyInformation'), h1bVisaInformation: optional('h1bVisaInformation'), likelyInterviewProcess: optional('likelyInterviewProcess'), postingAvailability: value('postingAvailability') || 'Unknown', lastAvailabilityCheck: localToUtc(value('lastAvailabilityCheck')), availabilityCheckNotes: optional('availabilityCheckNotes') };
      const id = config.id || config.createdJobId;
      const job = await api('/jobs' + (id ? '/' + id : ''), { method: id ? 'PUT' : 'POST', body: JSON.stringify(body) });
      state.jobs = state.jobs.filter(item => item.id !== job.id).concat(job);
      if (!config.id) {
        // Retain this ID if the second request fails, so retrying never creates another job.
        config.createdJobId = job.id;
        const application = await api('/applications', { method: 'POST', body: JSON.stringify({ userId: state.userId, jobId: job.id, status: 'Saved', dateFound: todayDate(), dateApplied: null, interviewDate: null, notes: null }) });
        state.applications.push(application);
        if (config.thenApply) continueApplication = application.id;
      }
      message = config.id ? 'Job details updated.' : 'A new possibility, saved.';
    } else if (config.type === 'application') {
      const body = {
        userId: state.userId, jobId: config.id ? state.applications.find(item => item.id === config.id).jobId : Number(value('jobId')), status: value('status'),
        dateApplied: dateToUtc(value('dateApplied')), interviewDate: localToUtc(value('interviewDate')), notes: optional('notes'), dateFound: optional('dateFound'), priority: optional('priority'), interviewPrepNotes: optional('interviewPrepNotes'), resumeTips: optional('resumeTips'), dsaTopicsToKnow: optional('dsaTopicsToKnow'), additionalSourceNotes: optional('additionalSourceNotes')
      };
      const application = await api('/applications' + (config.id ? '/' + config.id : ''), { method: config.id ? 'PUT' : 'POST', body: JSON.stringify(body) });
      state.applications = state.applications.filter(item => item.id !== application.id).concat(application);
      message = config.id ? 'Your application is up to date.' : 'One more opportunity in motion.';
    } else if (config.type === 'reminder') {
      const body = { userId: state.userId, applicationId: Number(value('applicationId')), title: value('title'), dueDate: localToUtc(value('dueDate')), completed: data.has('completed') };
      const reminder = await api('/reminders' + (config.id ? '/' + config.id : ''), { method: config.id ? 'PUT' : 'POST', body: JSON.stringify(body) });
      state.reminders = state.reminders.filter(item => item.id !== reminder.id).concat(reminder);
      message = config.id ? 'Reminder updated.' : 'A little reminder, right when you need it.';
    }
    $('#editor').close();
    editorState = null;
    toast(message);
    await loadWorkspace();
    if (config.type === 'profile' && ['job','company'].includes(state.view)) location.hash = 'jobs';
    if (continueApplication) openApplication(continueApplication);
  } catch (exception) {
    error.textContent = (config.type === 'job' && config.createdJobId ? 'The job is saved, but could not be added to your list. Try again here. ' : '') + exception.message;
    error.hidden = false;
    error.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  } finally {
    saving = false;
    form.querySelectorAll('button').forEach(button => { button.disabled = false; });
    const submit = $('#submit-editor');
    if (submit && $('#editor-form') === form) submit.innerHTML = icon('check') + (config.id ? 'Save changes' : ({ profile: 'Create profile', job: config.thenApply ? 'Save & continue' : 'Save job', application: 'Add application', reminder: 'Add reminder' })[config.type]);
  }
}

function toast(message, error = false) {
  const item = document.createElement('div');
  item.className = 'toast' + (error ? ' error' : '');
  item.innerHTML = icon(error ? 'info' : 'check') + '<span></span>';
  $('span', item).textContent = message;
  $('#toast-region').append(item);
  setTimeout(() => item.remove(), error ? 9000 : 4500);
}

function confirmDelete(title, message) {
  const dialog = $('#confirmation');
  dialog.innerHTML = `<h2 id="confirm-title">${escape(title)}</h2><p>${escape(message)}</p><div class="confirmation-actions"><button class="button" data-confirm="cancel">Keep it</button><button class="button danger" data-confirm="delete">Delete</button></div>`;
  dialog.returnValue = '';
  dialog.showModal();
  return new Promise(resolve => {
    dialog.addEventListener('close', () => resolve(dialog.returnValue === 'delete'), { once: true });
    $('[data-confirm="cancel"]', dialog).focus();
  });
}

async function deleteRecord(type, id) {
  if (busyAction) return;
  if (type === 'application' && (state.reminders.some(reminder => reminder.applicationId === id) || state.errors.some(error => error.startsWith('Reminders')))) {
    toast('Remove this application’s reminders first, then delete the application.', true);
    return;
  }
  const accepted = await confirmDelete(type === 'application' ? 'Let this opportunity go?' : 'Remove this reminder?', type === 'application' ? 'This application will be removed from your list. The job posting will stay in the shared library.' : 'This reminder will be removed. Your application will stay right where it is.');
  if (!accepted) return;
  busyAction = true;
  try {
    await api('/' + (type === 'application' ? 'applications' : 'reminders') + '/' + id, { method: 'DELETE' });
    if (type === 'application') state.applications = state.applications.filter(application => application.id !== id);
    else state.reminders = state.reminders.filter(reminder => reminder.id !== id);
    if ($('#editor').open) $('#editor').close();
    editorState = null;
    render();
    toast(type === 'application' ? 'Application removed from your list.' : 'Reminder removed.');
  } catch (exception) {
    toast(exception.message, true);
  } finally { busyAction = false; }
}

async function toggleReminder(id, button) {
  const reminder = state.reminders.find(reminder => reminder.id === id);
  if (!reminder || busyAction) return;
  busyAction = true;
  button.disabled = true;
  try {
    const updated = await api('/reminders/' + id, { method: 'PUT', body: JSON.stringify({
      userId: reminder.userId, applicationId: reminder.applicationId, title: reminder.title, dueDate: reminder.dueDate, completed: !reminder.completed
    }) });
    state.reminders = state.reminders.map(item => item.id === id ? updated : item);
    render();
    toast(updated.completed ? 'One less thing on your list. Nicely done.' : 'Reminder moved back to upcoming.');
  } catch (exception) {
    button.disabled = false;
    toast(exception.message, true);
  } finally { busyAction = false; }
}

function updateNavigationAccess() {
  $('#sidebar').inert = matchMedia('(max-width: 760px)').matches && !document.body.classList.contains('nav-open');
}

function closeNavigation() {
  document.body.classList.remove('nav-open');
  $('#sidebar-scrim').hidden = true;
  $('#mobile-menu').setAttribute('aria-expanded', 'false');
  updateNavigationAccess();
}



let pendingScrollRestore = null;
let listPage = 1;
let listSort = 'newest';
let listReturnRoute = 'jobs';
let activeRoute = '';
let selectedCompany = '';
let selectedJobId = null;
const listMemory = new Map();
const companyKey = name => String(name || '').trim().replace(/\s+/g, ' ').toLowerCase();
const foundLabel = value => value ? formatDate(value + 'T12:00:00', { year: 'numeric' }) : 'Not recorded';
const trackedEntries = () => {
  const entries = new Map();
  for (const application of state.applications) {
    const job = jobFor(application);
    if (job && (!entries.has(job.id) || entries.get(job.id).application.id < application.id)) entries.set(job.id, { job, application });
  }
  return [...entries.values()];
};
function rememberList() {
  if (!['jobs', 'applications', 'companies', 'company', 'reminders'].includes(state.view) || !activeRoute) return;
  const snapshot = { query: state.query, status: state.status, filters: { ...trackerFilters }, page: listPage, sort: listSort, scroll: window.scrollY, reminderTab: state.reminderTab };
  listMemory.set(activeRoute, snapshot);
  try { sessionStorage.setItem('lutzpath.list.' + state.userId + '.' + activeRoute, JSON.stringify(snapshot)); } catch {}
}
function restoreList(route) {
  let snapshot = listMemory.get(route);
  if (!snapshot) { try { snapshot = JSON.parse(sessionStorage.getItem('lutzpath.list.' + state.userId + '.' + route)); } catch {} }
  state.query = snapshot?.query || '';
  state.status = snapshot?.status || '';
  Object.assign(trackerFilters, { priority: '', availability: '', city: '' }, snapshot?.filters || {});
  listPage = snapshot?.page || 1;
  listSort = snapshot?.sort || 'newest';
  state.reminderTab = snapshot?.reminderTab || 'Upcoming';
  return snapshot?.scroll || 0;
}
function changeView(route, initial = false) {
  if (!initial) rememberList();
  const [kind, ...parts] = route.split('/');
  const detail = parts.join('/');
  let view = kind || 'jobs';
  if (!Object.hasOwn(views, view) && !['job', 'company'].includes(view)) view = 'jobs';
  if (view === 'job') {
    selectedJobId = /^\d+$/.test(detail) ? Number(detail) : null;
    if (activeRoute && state.view !== 'job') {
      listReturnRoute = activeRoute;
      savePreference('lutzpath.return.' + state.userId, listReturnRoute);
    } else listReturnRoute = readPreference('lutzpath.return.' + state.userId) || 'jobs';
  }
  if (view === 'company') { try { selectedCompany = decodeURIComponent(detail); } catch { selectedCompany = ''; } }
  state.view = view;
  activeRoute = ['job', 'company'].includes(view) ? view + '/' + detail : view;
  const scroll = view === 'job' ? 0 : restoreList(activeRoute);
  closeNavigation();
  render();
  document.title = (views[view] || (view === 'job' ? 'Job details' : 'Company jobs')) + ' — LutzPath';
  if (state.loading) pendingScrollRestore = scroll;
  else requestAnimationFrame(() => window.scrollTo({ top: scroll, behavior: 'instant' }));
}
window.addEventListener('pagehide', rememberList);
const selectControl = (id, label, options, value) => `<label class="list-control"><span>${escape(label)}</span><select class="filter-select" id="${id}" aria-label="${escape(label)}">${options.map(([key,text]) => `<option value="${escape(key)}" ${key === value ? 'selected' : ''}>${escape(text)}</option>`).join('')}</select></label>`;
function filterEntries(entries) {
  const query = state.query.toLowerCase().trim();
  return entries.filter(({ job, application }) =>
    (!query || [job.title, job.company, job.location, application.priority, application.notes, application.interviewPrepNotes, application.resumeTips, application.dsaTopicsToKnow].some(value => value?.toLowerCase().includes(query))) &&
    (!state.status || application.status === state.status) &&
    (!trackerFilters.availability || job.postingAvailability === trackerFilters.availability) &&
    (!trackerFilters.priority || application.priority === trackerFilters.priority) &&
    (!trackerFilters.city || job.location === trackerFilters.city)
  ).sort((a,b) => {
    const left = a.application.dateFound, right = b.application.dateFound;
    if (!left && right) return 1;
    if (left && !right) return -1;
    const dates = (left || '').localeCompare(right || '');
    return (listSort === 'oldest' ? dates : -dates) || b.job.id - a.job.id;
  });
}
function workspaceFilters(entries) {
  const advanced = [['priority','Priority',entries.map(e=>e.application.priority)],['city','Location',entries.map(e=>e.job.location)]];
  return `<div class="job-toolbar">${searchControl('Search jobs or companies…')}
    ${selectControl('status-filter','Filter by status',[['','All statuses'],...statuses.map(s=>[s,statusLabel(s)])],state.status)}
    <label class="list-control"><span>Availability</span><select class="filter-select" data-tracker-filter="availability" aria-label="Filter by availability">${[['','All availability'],['Open','Open'],['Closed','Closed'],['Unknown','Unknown']].map(([v,l])=>`<option value="${v}" ${trackerFilters.availability===v?'selected':''}>${l}</option>`).join('')}</select></label>
    ${selectControl('job-sort','Sort jobs',[['newest','Newest found first'],['oldest','Oldest found first']],listSort)}
    </div><details class="more-filters"><summary>More filters${trackerFilters.priority || trackerFilters.city ? ' · active' : ''}</summary><div class="tracker-filters">${advanced.map(([key,label,values])=>`<label>${label}<select class="filter-select" data-tracker-filter="${key}" aria-label="Filter by ${label.toLowerCase()}"><option value="">All</option>${[...new Set(values.filter(Boolean))].sort().map(value=>`<option value="${escape(value)}" ${trackerFilters[key]===value?'selected':''}>${escape(value)}</option>`).join('')}</select></label>`).join('')}</div></details>`;
}
function workspaceTable(entries) {
  const filtered = filterEntries(entries);
  const pages = Math.max(1, Math.ceil(filtered.length / 25));
  listPage = Math.min(pages, Math.max(1, listPage));
  const visible = filtered.slice((listPage-1)*25,listPage*25);
  return (visible.length ? `<div class="table-wrap"><table class="applications-table job-list"><thead><tr><th scope="col">Job</th><th scope="col">Location</th><th scope="col">Found</th><th scope="col">Availability</th><th scope="col">My status</th></tr></thead><tbody>${visible.map(({job,application})=>`<tr data-job-id="${job.id}"><td><button class="role-name" data-action="job-detail" data-id="${job.id}">${escape(job.title)}</button><button class="company-link" data-action="company-detail" data-company="${escape(companyKey(job.company))}">${escape(job.company)}</button></td><td class="tracker-location">${escape(job.location || 'Not specified')}</td><td>${foundLabel(application.dateFound)}</td><td><span class="availability availability-${escape((job.postingAvailability || 'Unknown').toLowerCase())}">${escape(job.postingAvailability || 'Unknown')}</span></td><td>${badge(application.status)}</td></tr>`).join('')}</tbody></table></div>` :
    empty('briefcase', entries.length ? 'No matching jobs' : 'No jobs here yet', entries.length ? 'Try another search or clear your filters.' : 'Save a job to start your list.', entries.length ? actionButton('Clear filters','clear-filters') : actionButton('Add your first job','new-job'))) +
    `<div class="panel-bottom pagination"><span>${filtered.length ? (listPage-1)*25+1 : 0}–${Math.min(listPage*25,filtered.length)} of ${filtered.length} jobs</span><div><button class="button small" data-action="list-page" data-id="${listPage-1}" ${listPage===1?'disabled':''}>Previous</button><span>Page ${listPage} of ${pages}</span><button class="button small" data-action="list-page" data-id="${listPage+1}" ${listPage===pages?'disabled':''}>Next</button></div></div>`;
}
function renderWorkspaceJobs() {
  const entries = trackedEntries();
  return pageHeading('Your workspace','Jobs','Every tracked opportunity, ordered by the date you found it.',actionButton('Preview Google Sheet','preview-sheet','refresh') + actionButton('Save a job','new-job','plus',true)) + sheetSyncPanel() +
    `<section class="panel">${workspaceFilters(entries)}${workspaceTable(entries)}</section>`;
}
function renderSubmittedApplications() {
  const entries = trackedEntries().filter(({application})=>!['Saved','NotApplied'].includes(application.status));
  return pageHeading('Your progress','Applications','Submitted applications, interviews, and outcomes.',actionButton('Add application','new-application','plus',true)) +
    `<section class="panel">${workspaceFilters(entries)}${workspaceTable(entries)}</section>`;
}
function renderCompanyDirectory() {
  const groups = new Map();
  for (const entry of trackedEntries()) {
    const key = companyKey(entry.job.company);
    if (!groups.has(key)) groups.set(key, { key, name: entry.job.company.trim().replace(/\s+/g,' '), entries: [] });
    groups.get(key).entries.push(entry);
  }
  const companies = [...groups.values()].filter(c=>c.name.toLowerCase().includes(state.query.toLowerCase().trim())).sort((a,b)=>a.name.localeCompare(b.name));
  const pages = Math.max(1,Math.ceil(companies.length/25)); listPage = Math.min(listPage,pages);
  return pageHeading('Explore your shortlist','Companies','Choose a company to see its jobs.', '') +
    `<section class="panel"><div class="job-toolbar">${searchControl('Search companies…')}<span>${companies.length} companies</span></div>
    ${companies.length ? `<div class="company-directory">${companies.slice((listPage-1)*25,listPage*25).map(c=>{
      const latest = c.entries.map(e=>e.application.dateFound).filter(Boolean).sort().at(-1);
      return `<button class="company-tile" data-action="company-detail" data-company="${escape(c.key)}">${companyAvatar(c.name)}<span><strong>${escape(c.name)}</strong><small>${c.entries.length} tracked jobs · ${c.entries.filter(e=>e.job.postingAvailability==='Open').length} open</small><small>Latest found: ${foundLabel(latest)}</small></span>${icon('chevron')}</button>`;
    }).join('')}</div>` : empty('briefcase','No companies found','Try another search or save a job.')}
    <div class="panel-bottom pagination"><span>Page ${listPage} of ${pages}</span><div><button class="button small" data-action="list-page" data-id="${listPage-1}" ${listPage===1?'disabled':''}>Previous</button><button class="button small" data-action="list-page" data-id="${listPage+1}" ${listPage===pages?'disabled':''}>Next</button></div></div></section>`;
}
function renderCompanyJobs() {
  const entries = trackedEntries().filter(e=>companyKey(e.job.company)===selectedCompany);
  return `<a class="back-link" href="#companies">← Companies</a>` + pageHeading('Company jobs',escape(entries[0]?.job.company || 'Company unavailable'),entries.length + ' tracked jobs in your profile.', '') +
    `<section class="panel">${workspaceFilters(entries)}${workspaceTable(entries)}</section>`;
}
function detailText(label,value) {
  return `<div class="detail-item"><dt>${escape(label)}</dt><dd>${escape(value || 'Not recorded')}</dd></div>`;
}
function renderJobDetail() {
  const entry = trackedEntries().find(e=>e.job.id===selectedJobId);
  if (!entry) return `<a class="back-link" href="#jobs">← Jobs</a><section class="panel">${empty('briefcase',state.errors.length ? 'Job details unavailable' : 'Job not found',state.errors.length ? 'Refresh your workspace to try again.' : 'This job is not in the active profile.')}</section>`;
  const {job,application} = entry;
  const url = safeUrl(job.jobUrl);
  const reminders = state.reminders.filter(r=>r.applicationId===application.id);
  return `<button class="back-link" data-action="return-list">← Back to list</button>` +
    pageHeading(job.company,escape(job.title),job.location || 'Location not recorded',
      actionButton('Tailor resume','tailor-resume','edit',true,`data-id="${application.id}"`) + actionButton('Update application','edit-application','edit',false,`data-id="${application.id}"`)) +
    `<div class="detail-actions">${actionButton('Edit job details','edit-job','edit',false,`data-id="${job.id}"`)}${actionButton('Add reminder','new-reminder','plus',false,`data-id="${application.id}"`)}${url ? `<a class="button" href="${escape(url)}" target="_blank" rel="noopener noreferrer">Open original posting ${icon('upRight')}</a>` : ''}<button class="text-button" data-action="company-detail" data-company="${escape(companyKey(job.company))}">All jobs at ${escape(job.company)}</button></div>
    <div class="detail-grid"><section class="panel detail-panel"><h2>Job information</h2><dl>${detailText('Location',job.location)}${detailText('Date found',foundLabel(application.dateFound))}${detailText('Date posted',foundLabel(job.datePosted))}${detailText('Availability',job.postingAvailability)}${detailText('Description',job.description)}</dl><details><summary>Availability evidence</summary><dl>${detailText('Last checked',job.lastAvailabilityCheck ? formatDate(job.lastAvailabilityCheck,{year:'numeric'})+' · '+formatTime(job.lastAvailabilityCheck) : null)}${detailText('Source evidence',job.availabilityCheckNotes)}</dl></details></section>
    <section class="panel detail-panel"><h2>My application</h2>${badge(application.status)}<dl>${detailText('Applied',application.dateApplied ? formatDate(application.dateApplied,{year:'numeric'}) : null)}${detailText('Interview',application.interviewDate ? formatDate(application.interviewDate,{year:'numeric'})+' · '+formatTime(application.interviewDate) : null)}${detailText('Priority',application.priority)}${detailText('Comments',application.notes)}</dl></section>
    <section class="panel detail-panel"><h2>Visa information</h2><dl>${detailText('OPT friendly',job.optFriendlyInformation)}${detailText('H-1B visa',job.h1bVisaInformation)}</dl></section>
    <section class="panel detail-panel"><h2>Preparation</h2><details><summary>Interview process & preparation</summary><dl>${detailText('Likely interview process',job.likelyInterviewProcess)}${detailText('Interview prep notes',application.interviewPrepNotes)}${detailText('Resume tips',application.resumeTips)}${detailText('DSA topics to know',application.dsaTopicsToKnow)}${detailText('Additional source notes',application.additionalSourceNotes)}</dl></details></section>
    <section class="panel detail-panel detail-reminders"><h2>Reminders</h2>${state.errors.some(e=>e.startsWith('Reminders')) ? '<p>Reminders could not be loaded. Refresh to try again.</p>' : reminders.length ? reminders.map(reminderRow).join('') : '<p>No reminders for this application yet.</p>'}</section></div>`;
}



function sheetSyncPanel() {
  const status=state.sheetSync;
  if(!status?.enabled)return '';
  const owner=status.userId===state.userId;
  const last=status.lastSuccessUtc ? formatDate(status.lastSuccessUtc,{year:'numeric'})+' · '+formatTime(status.lastSuccessUtc) : 'Waiting for first sync';
  return `<section class="sync-panel" aria-label="Google Sheets sync"><div><strong>Google Sheets · ${status.automaticUpdatesEnabled?'automatic updates on':'manual sync'}</strong><p>${escape(last)}${status.lastSuccessUtc ? ' · '+(status.created||0)+' new, '+(status.updated||0)+' updated, '+(status.skipped||0)+' skipped' : ''}</p>${!owner?'<p>Sync updates the configured primary profile.</p>':''}${status.lastError?`<p class="sync-error">${escape(status.lastError)}</p>`:''}${status.warnings?.length?`<details><summary>${status.warnings.length} rows need review</summary><ul>${status.warnings.slice(0,30).map(w=>`<li>${escape(w)}</li>`).join('')}</ul></details>`:''}</div>${owner?actionButton('Sync now','sync-sheet','refresh'):''}</section>`;
}
async function syncSheetNow(button) {
  if(busyAction)return;busyAction=true;button.disabled=true;
  $('#refresh-button').classList.add('spinning');
  try {
    const result=await api('/integrations/google-sheets/sync',{method:'POST',signal:AbortSignal.timeout(100000)});
    toast(result.created+' new jobs, '+result.updated+' updated.');
  } catch(error){toast(error.message,true);}
  finally{try{await loadWorkspace();}finally{busyAction=false;button.disabled=false;$('#refresh-button').classList.remove('spinning');}}
}
async function refreshWorkspace(button) {
  if(busyAction)return;
  if(state.sheetSync?.enabled && state.sheetSync.userId===state.userId) return syncSheetNow(button);
  await loadWorkspace();
}
setInterval(async()=>{
  if(document.visibilityState!=='visible'||!state.sheetSync?.automaticUpdatesEnabled||saving||$('#editor').open)return;
  try {
    const status=await api('/integrations/google-sheets/status');
    if(status.lastSuccessUtc!==state.sheetSync.lastSuccessUtc || status.lastError!==state.sheetSync.lastError)await loadWorkspace();
  } catch { /* The next poll or refresh can recover without interrupting editing. */ }
},30000);

async function openSheetPreview() {
  if (!ensureProfile()) return;
  const previewConfig = {type:'sheet-preview'};
  showEditor(previewConfig, 'Google Sheets preview', 'Read-only comparison with your saved jobs.',
    '<p class="form-hint">Loading Sheet1… Your saved data will not change.</p>', 'Close');
  const close = $('#submit-editor');
  close.type = 'button'; close.dataset.action = 'close-editor'; close.textContent = 'Close';
  const body = $('#editor-fields');
  try {
    const report = await api('/integrations/google-sheets/preview?userId=' + state.userId, {signal:AbortSignal.timeout(60000)});
    if (!$('#editor').open || editorState !== previewConfig) return;
    const review = report.rows.filter(row => row.match !== 'Matched' || row.changes.length || row.warnings.length);
    const html = `<p class="form-hint"><strong>Connected to Sheet1.</strong> This preview does not change records. ${report.automaticUpdatesEnabled ? 'Automatic posting updates are enabled.' : 'Automatic updates are off.'}</p>
      <dl class="preview-summary"><div><dt>Source jobs</dt><dd>${report.sourceRows}</dd></div><div><dt>Matched</dt><dd>${report.matchedRows}</dd></div><div><dt>Unmatched</dt><dd>${report.unmatchedRows}</dd></div><div><dt>Ambiguous</dt><dd>${report.ambiguousRows}</dd></div><div><dt>With differences</dt><dd>${report.rowsWithDifferences}</dd></div></dl>
      <details><summary>How this preview works</summary><ul>${report.warnings.map(w=>`<li>${escape(w)}</li>`).join('')}</ul></details>
      <h3>Review items</h3>${review.length ? review.slice(0,100).map(row=>`<details class="tracker-details"><summary>Row ${row.rowNumber}: ${escape(row.company)} — ${escape(row.title)} · ${escape(row.match)}</summary>
      ${row.warnings.map(w=>`<p class="field-hint">${escape(w)}</p>`).join('')}
      ${row.changes.map(change=>`<div class="detail-item"><strong>${escape(change.field)}${change.protected ? ' · protected' : ''}</strong><p>Saved: ${escape(change.databaseValue || '(empty)')}</p><p>Sheet: ${escape(change.sheetValue || '(empty)')}</p></div>`).join('')}</details>`).join('') : '<p>No differences or row warnings found.</p>'}
      ${review.length > 100 ? '<p>Showing the first 100 review items. The API report contains all rows.</p>' : ''}
      <p class="field-hint">Existing application notes, status, dates, preparation notes, and reminders are protected.</p>`;
    // The form's existing field container is shared with the other dialogs.
    const container = body || $('#editor-form .form-body');
    if (container) container.innerHTML = html;
  } catch (error) {
    if (!$('#editor').open || editorState !== previewConfig) return;
    const container = body || $('#editor-form .form-body');
    if (container) container.innerHTML = '<p class="form-hint">' + escape(error.message) + '</p><p>Your saved data is unchanged. Close and try again.</p>';
  }
}

document.addEventListener('click', async event => {
  const confirmButton = event.target.closest('[data-confirm]');
  if (confirmButton) { $('#confirmation').close(confirmButton.dataset.confirm); return; }
  const sidebarLink = event.target.closest('#navigation a');
  if (sidebarLink && sidebarLink.hash === location.hash) closeNavigation();
  const navigation = event.target.closest('[data-view]');
  if (navigation) { location.hash = navigation.dataset.view; return; }
  const appTab = event.target.closest('[data-application-tab]');
  if (appTab) { state.applicationTab = appTab.dataset.applicationTab; render(); return; }
  const reminderTab = event.target.closest('[data-reminder-tab]');
  if (reminderTab) { state.reminderTab = reminderTab.dataset.reminderTab; render(); return; }
  const button = event.target.closest('[data-action]');
  if (!button || button.disabled) return;
  const id = Number(button.dataset.id);
  switch (button.dataset.action) {
    case 'sync-sheet': await syncSheetNow(button); break;
    case 'preview-sheet': openSheetPreview(); break;
    case 'tailor-resume': {
      const application = state.applications.find(a => a.id === id);
      if (application) await openTailoring(application, jobFor(application));
      break;
    }
    case 'new-profile': openProfile(); break;
    case 'edit-profile': openProfile(true); break;
    case 'new-job': openJob(); break;
    case 'edit-job': openJob(id); break;
    case 'new-job-for-application': openJob(null, true); break;
    case 'new-application': openApplication(); break;
    case 'edit-application': openApplication(id); break;
    case 'new-reminder': openReminder(null, id || null); break;
    case 'job-detail': location.hash = 'job/' + id; break;
    case 'company-detail': location.hash = 'company/' + encodeURIComponent(button.dataset.company); break;
    case 'return-list': location.hash = listReturnRoute; break;
    case 'list-page': listPage = Math.max(1, id); render(); window.scrollTo(0, 0); break;
    case 'edit-reminder': openReminder(id); break;
    case 'close-editor': if (!saving) $('#editor').close(); break;
    case 'delete-application': await deleteRecord('application', id); break;
    case 'delete-reminder': await deleteRecord('reminder', id); break;
    case 'toggle-reminder': await toggleReminder(id, button); break;
    case 'refresh': await refreshWorkspace(button); break;
    case 'clear-filters': Object.keys(trackerFilters).forEach(key => trackerFilters[key] = ''); state.query = ''; state.status = ''; state.applicationTab = 'All'; listPage = 1; render(); break;
  }
});
document.addEventListener('submit', event => {
  if (event.target.id === 'editor-form') saveEditor(event);
});
document.addEventListener('input', event => {
  if (event.target.id === 'workspace-search') { state.query = event.target.value; listPage = 1; render(); }
});
document.addEventListener('change', async event => {
  if (event.target.dataset.trackerFilter) { trackerFilters[event.target.dataset.trackerFilter] = event.target.value; listPage = 1; render(); }
  if (event.target.id === 'job-sort') { listSort = event.target.value; listPage = 1; render(); }
  if (event.target.id === 'status-filter') { state.status = event.target.value; listPage = 1; render(); }
  if (event.target.id === 'profile-select') {
    Object.keys(trackerFilters).forEach(key => trackerFilters[key] = '');
    listMemory.clear(); listPage = 1; listReturnRoute = 'jobs';
    state.userId = Number(event.target.value);
    state.applications = [];
    state.reminders = [];
    state.query = ''; state.status = ''; selectedJobId = null; selectedCompany = '';
    savePreference('lutzpath.profile', state.userId);
    render();
    await loadWorkspace();
    if (state.view === 'job' || state.view === 'company') { location.hash = 'jobs'; }
  }
});
window.addEventListener('hashchange', () => changeView(location.hash.slice(1)));
$('#editor').addEventListener('cancel', event => { if (saving) event.preventDefault(); });
$('#editor').addEventListener('click', event => {
  if (event.target === $('#editor') && !saving) {
    const rect = event.target.getBoundingClientRect();
    if (event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom) event.target.close();
  }
});
$('#mobile-menu').addEventListener('click', () => {
  const open = document.body.classList.toggle('nav-open');
  $('#sidebar-scrim').hidden = !open;
  $('#mobile-menu').setAttribute('aria-expanded', String(open));
  updateNavigationAccess();
  if (open) $('#navigation a')?.focus();
});
$('#sidebar-scrim').addEventListener('click', closeNavigation);
document.addEventListener('keydown', event => {
  if (event.key === 'Tab' && document.body.classList.contains('nav-open')) {
    const targets = [...$('#sidebar').querySelectorAll('a, button')].filter(element => element.getClientRects().length);
    const first = targets[0];
    const last = targets.at(-1);
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
  }
  if (event.key === 'Escape' && document.body.classList.contains('nav-open')) { closeNavigation(); $('#mobile-menu').focus(); }
});
$('#encouragement-icon').innerHTML = icon('spark');
$('#profile-edit-icon').innerHTML = icon('down');
$('#refresh-button').innerHTML = icon('refresh');
$('#mobile-menu').innerHTML = icon('menu');
$('#add-profile-button').innerHTML = icon('plus');
$('#today-label').textContent = new Intl.DateTimeFormat(undefined, { weekday: 'short', month: 'short', day: 'numeric' }).format(new Date());
matchMedia('(max-width: 760px)').addEventListener('change', () => { closeNavigation(); updateNavigationAccess(); });
updateNavigationAccess();
changeView(location.hash.slice(1), true);
loadWorkspace({ welcome: true });
