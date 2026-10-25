const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'})[c]);
let context, version = 0, timer;
export function configureResumes(options) { context = options; }
async function request(path = '', options = {}) {
  const response = await fetch('/api/resumes' + path, {
    ...options, headers: options.body instanceof FormData ? {} : {'Content-Type':'application/json'},
    signal: AbortSignal.timeout(25000)
  });
  if (!response.ok) { const problem = await response.json().catch(() => ({})); throw new Error(problem.detail || problem.title || 'Resume Service is unavailable. Try again later.'); }
  return response.status === 204 ? null : response.json();
}
const userQuery = user => '?userId=' + user;
const button = (text, action, id = '', primary = false) => `<button class="button${primary ? ' primary' : ''}" type="button" data-resume-action="${action}" data-id="${id}">${esc(text)}</button>`;
const field = (label, html) => `<label class="field"><span>${esc(label)}</span>${html}</label>`;
function dialog(title, body, onSubmit, submit = 'Save') {
  document.querySelector('#resume-editor')?.remove();
  const node = document.createElement('dialog'); node.id = 'resume-editor'; node.className = 'resume-dialog';
  node.innerHTML = `<form><div class="dialog-heading"><div><h2>${esc(title)}</h2></div><button type="button" class="button" data-close>Close</button></div><div class="form-body">${body}<p role="alert" class="form-error" hidden></p></div>${onSubmit ? `<div class="dialog-footer"><button class="button primary" type="submit">${esc(submit)}</button></div>` : ''}</form>`;
  document.body.append(node); node.querySelector('[data-close]').onclick = () => node.close();
  node.addEventListener('close', () => node.remove());
  if (onSubmit) node.querySelector('form').onsubmit = async event => {
    event.preventDefault();
    const submitButton = node.querySelector('[type=submit]'); submitButton.disabled = true;
    const error = node.querySelector('[role=alert]'); error.hidden = true;
    try { await onSubmit(new FormData(event.target), node); }
    catch (ex) { error.textContent = ex.message; error.hidden = false; }
    finally { submitButton.disabled = false; }
  };
  node.showModal(); return node;
}

export function renderResumes() {
  queueMicrotask(() => mount());
  return `<div class="page-heading"><div><p class="eyebrow">YOUR EXPERIENCE, YOUR WORDS</p><h1>Resumes</h1><p>Keep your master resume, tailor a draft, and save the Word version you send.</p></div></div><div id="resume-workspace" aria-live="polite">Loading resumes…</div>`;
}
async function mount() {
  clearTimeout(timer);
  const generation = ++version, user = context.userId();
  const root = document.querySelector('#resume-workspace'); if (!root) return;
  if (!user) { root.textContent = 'Create or select a profile first.'; return; }
  try {
    const [data, settings] = await Promise.all([request(userQuery(user)), request('/settings')]);
    if (generation !== version || context.userId() !== user || !root.isConnected) return;
    root.innerHTML = `${!settings.configured ? '<div class="resume-notice">AI generation is not configured yet. You can upload your master and save tailoring profiles now. See docs/resumes.md for server setup.</div>' : `<div class="resume-notice">AI provider: OpenAI · ${esc(settings.model)}. Generating sends your approved master, job description and instructions to OpenAI.</div>`}
      <div class="detail-grid"><section class="panel detail-panel"><h2>Master resume</h2><p>Upload a Word file, review the extracted text, then approve it. Uploading again creates a new version.</p><form id="resume-upload">${field('Word document (.docx, up to 2 MB)', '<input name="file" type="file" accept=".docx" required>')}<button class="button primary">Upload master resume</button><p role="alert" hidden></p></form><div class="resume-items">${data.masters.map(m => `<article><strong>${esc(m.fileName)}</strong><p>Version ${m.id} · ${m.approved ? 'Approved' : 'Needs review'} · ${new Date(m.createdAt).toLocaleDateString()}</p>${button(m.approved ? 'View master' : 'Review & approve','master',m.id)} <a class="button" href="/api/resumes/masters/${m.id}/download${userQuery(user)}">Original Word file</a></article>`).join('') || '<p>No master uploaded yet.</p>'}</div></section>
      <section class="panel detail-panel"><h2>Tailoring profiles</h2><p>Save instructions for the kinds of roles you apply to. Factual accuracy rules always apply.</p>${button('New tailoring profile','profile')}<div class="resume-items">${data.profiles.map(p => `<article><strong>${esc(p.name)}</strong><p>${esc(p.instructions)}</p><small>${p.targetPages} page target · ${esc(p.font)}</small></article>`).join('') || '<p>No profiles yet.</p>'}</div></section></div>
      <section class="panel detail-panel"><h2>Tailored resumes</h2><p>Start from a job’s “Tailor resume” button. Review every claim before downloading.</p><div class="resume-items">${data.drafts.map(d => `<article><strong>${esc(d.jobTitle)}</strong><p>${esc(d.status)} · ${new Date(d.createdAt).toLocaleString()}</p>${d.error ? `<p class="form-error">${esc(d.error)}</p>` : ''}${button('Open draft','draft',d.id)}${data.documents.filter(doc => doc.draftId === d.id).map(doc => `<div class="resume-document"><a class="button" href="/api/resumes/documents/${doc.id}/download${userQuery(user)}">Word version ${doc.id}</a> ${doc.submitted ? '<span>Marked submitted</span>' : button('Mark submitted','submitted',doc.id)}</div>`).join('')}</article>`).join('') || '<p>Your tailored drafts will appear here.</p>'}</div></section>`;
    root.querySelectorAll('.detail-grid > section:nth-child(2) .resume-items article').forEach((article, index) => {
      article.querySelector('small').textContent = `${data.profiles[index].targetPages} page target · original Word formatting`;
      article.insertAdjacentHTML('beforeend', button('Edit instructions', 'edit-profile', data.profiles[index].id));
    });
    root.querySelector('#resume-upload').onsubmit = async event => {
      event.preventDefault(); const form = event.target, submit = form.querySelector('button'), error = form.querySelector('[role=alert]');
      submit.disabled = true; error.hidden = true;
      try { const master = await request('/masters' + userQuery(user), {method:'POST', body:new FormData(form)}); await mount(); openMaster(master, user); }
      catch (ex) { error.textContent = ex.message; error.hidden = false; }
      finally { submit.disabled = false; }
    };
    root.onclick = async event => {
      const target = event.target.closest('[data-resume-action]'); if (!target) return;
      const id = Number(target.dataset.id);
      if (target.dataset.resumeAction === 'master') openMaster(data.masters.find(m => m.id === id), user);
      if (target.dataset.resumeAction === 'profile') openProfile(user);
      if (target.dataset.resumeAction === 'edit-profile') openProfile(user, data.profiles.find(p => p.id === id));
      if (target.dataset.resumeAction === 'draft') await openDraft(id, user);
      if (target.dataset.resumeAction === 'submitted') {
        target.disabled = true;
        try { await request(`/documents/${id}/submitted` + userQuery(user), {method:'POST'}); await mount(); }
        catch (ex) { dialog('Could not update document', `<p>${esc(ex.message)}</p>`); target.disabled = false; }
      }
    };
    if (data.drafts.some(d => ['Queued','Generating'].includes(d.status))) timer = setTimeout(mount, 4000);
  } catch (ex) { if (generation === version && root.isConnected) { root.innerHTML = `<div class="resume-notice" role="alert">${esc(ex.message)}</div>${button('Try again','reload')}`; root.querySelector('button').onclick = mount; } }
}
function openMaster(master, user) {
  const body = `<p>Check reading order, headings, dates and contact information. This is the factual source for future drafts. Approval freezes this version.</p>${field('Master resume content', `<textarea name="content" rows="20" ${master.approved ? 'readonly' : ''} required minlength="10" maxlength="60000">${esc(master.content)}</textarea>`)}`;
  dialog('Review master resume', body, master.approved ? null : async (form, node) => {
    await request(`/masters/${master.id}/approve` + userQuery(user), {method:'POST', body:JSON.stringify({content:form.get('content')})}); node.close(); await mount();
  }, 'Approve master content');
  document.querySelector('#resume-editor .form-body > p').textContent = 'Review the content without adding, deleting or reordering paragraph lines. The original Word file supplies the formatting. Correct complex layouts in Word and upload again. Approval freezes this version.';
}
function openProfile(user, profile = null) {
  dialog('New tailoring profile', `${field('Profile name','<input name="name" required maxlength="80" placeholder="Software engineering">')}${field('Your tailoring instructions','<textarea name="instructions" required maxlength="6000" rows="8">Use concise language. Preserve names, dates, education and contact details. Emphasize relevant projects and experience. Do not invent skills or metrics.</textarea>')}${field('Page target','<select name="pages"><option value="1">1 page</option><option value="2">2 pages</option></select>')}${field('Font','<select name="font"><option>Calibri</option><option>Arial</option><option>Aptos</option></select>')}<p>The page count is a target. Word export uses a simple layout; check pagination in Word.</p>`, async (form, node) => {
    await request('/profiles' + (profile ? '/' + profile.id : '') + userQuery(user), {method:profile ? 'PUT' : 'POST', body:JSON.stringify({name:form.get('name'),instructions:form.get('instructions'),targetPages:Number(form.get('pages')),font:form.get('font')})}); node.close(); await mount();
  }, 'Save profile');
  const node = document.querySelector('#resume-editor');
  node.querySelector('[name=instructions]').maxLength = 20000;
  node.querySelector('[name=font]').closest('label').hidden = true;
  node.querySelector('[name=font]').closest('label').style.display = 'none';
  node.querySelector('.form-body > p').textContent = 'New drafts retain the fonts and formatting from your master Word file. The page count is a writing target; check page breaks in Word. Use this field for writing preferences; the app controls the AI output structure.';
  if (profile) {
    node.querySelector('h2').textContent = 'Edit tailoring profile';
    node.querySelector('[name=name]').value = profile.name;
    node.querySelector('[name=instructions]').value = profile.instructions;
    node.querySelector('[name=pages]').value = String(profile.targetPages);
    node.querySelector('[name=font]').value = profile.font;
  }
}
export async function openTailoring(application, job) {
  const user = context.userId();
  try {
    const [data, settings] = await Promise.all([request(userQuery(user)), request('/settings')]);
    if (user !== context.userId()) return;
    const masters = data.masters.filter(m => m.approved);
    if (!masters.length || !data.profiles.length || !settings.configured) {
      dialog('Set up resume tailoring', `<p>${!masters.length ? 'Upload and approve your master resume. ' : ''}${!data.profiles.length ? 'Create a tailoring profile. ' : ''}${!settings.configured ? 'Configure the AI key and model on the server (docs/resumes.md). ' : ''}</p><a class="button primary" href="#resumes" data-setup>Open Resumes</a>`);
      document.querySelector('[data-setup]').onclick = () => document.querySelector('#resume-editor').close(); return;
    }
    const requestId = crypto.randomUUID();
    dialog('Tailor resume · ' + job.company, `<p>${esc(job.title)}</p>${field('Master version',`<select name="master">${masters.map(m=>`<option value="${m.id}">${esc(m.fileName)} · v${m.id}</option>`).join('')}</select>`)}${field('Tailoring profile',`<select name="profile">${data.profiles.map(p=>`<option value="${p.id}">${esc(p.name)}</option>`).join('')}</select>`)}${field('Full job description',`<textarea name="description" rows="12" required minlength="80" maxlength="40000" placeholder="Paste the actual job description, responsibilities and qualifications.">${esc(job.description || '')}</textarea>`)}<p>This sends the selected master, description and profile to OpenAI. Your API account is charged for generation. Review the result before using it.</p>`, async (form, node) => {
      await request('/drafts', {method:'POST', body:JSON.stringify({userId:user,applicationId:application.id,masterId:Number(form.get('master')),profileId:Number(form.get('profile')),requestId,jobDescription:form.get('description')})});
      node.close(); location.hash = 'resumes'; if (document.querySelector('#resume-workspace')) await mount();
    }, 'Generate draft');
  } catch (ex) { dialog('Resume tailoring unavailable', `<p>${esc(ex.message)}</p>`); }
}
async function openDraft(id, user) {
  try {
    const draft = await request(`/drafts/${id}` + userQuery(user));
    const body = `<p>${esc(draft.jobTitle)} · ${esc(draft.status)}</p>${draft.error ? `<p role="alert">${esc(draft.error)}</p>` : ''}${draft.status === 'Ready' ? `${field('Resume content',`<textarea name="content" rows="20" required minlength="10" maxlength="60000">${esc(draft.content)}</textarea>`)}<details><summary>Changes, gaps & source references</summary><pre class="resume-text">${esc(draft.reviewNotes)}</pre></details><label class="resume-confirm"><input name="reviewed" type="checkbox" required> I have reviewed the facts and content before exporting.</label><p>Saving and exporting creates a new Word version. Previous exports stay unchanged.</p>` : '<p>Queued and generating drafts update in the list automatically. Failed drafts require a new generation from the job page.</p>'}<details><summary>Original generation inputs</summary><h3>Master content</h3><pre class="resume-text">${esc(draft.masterSnapshot)}</pre><h3>Job description</h3><pre class="resume-text">${esc(draft.jobDescription)}</pre><h3>Profile snapshot</h3><pre class="resume-text">${esc(draft.profileSnapshot)}</pre></details>`;
    dialog('Review tailored resume', body, draft.status === 'Ready' ? async (form, node) => {
      await request(`/drafts/${id}` + userQuery(user), {method:'PUT',body:JSON.stringify({content:form.get('content')})});
      const doc = await request(`/drafts/${id}/export` + userQuery(user), {method:'POST'});
      node.close(); await mount();
      dialog('Word resume saved', `<p>Your exported version is saved in Resumes. Review its page breaks in Word.</p><a class="button primary" href="/api/resumes/documents/${doc.id}/download${userQuery(user)}">Download Word resume</a>`);
    } : null, 'Save & export Word');
    if (draft.status === 'Ready' && draft.templateParagraphsJson) {
      const paragraphs = JSON.parse(draft.templateParagraphsJson);
      const node = document.querySelector('#resume-editor');
      const content = node.querySelector('[name=content]');
      const originalField = content.closest('label'); originalField.hidden = true; originalField.style.display = 'none';
      const editor = document.createElement('div'); editor.className = 'resume-paragraph-editor';
      const lines = draft.content.split('\n');
      editor.innerHTML = `<p>Word formatting is retained from your master. Edit wording within each paragraph; protected paragraphs stay unchanged. This is a content editor, not a Word layout preview.</p>` + paragraphs.map((p, i) => field(`Paragraph ${p.id}${p.editable ? '' : ' · protected formatting'}`, `<textarea rows="3" data-template-paragraph="${p.id}" ${p.editable ? '' : 'readonly'} required>${esc(lines[i] ?? p.text)}</textarea>${p.editable ? '' : `<small>${esc(p.reason)}</small>`}`)).join('');
      originalField.after(editor);
      editor.addEventListener('input', () => { content.value = [...editor.querySelectorAll('textarea')].map(t => t.value).join('\n'); });
    }
  } catch (ex) { dialog('Could not load draft', `<p>${esc(ex.message)}</p>`); }
}
