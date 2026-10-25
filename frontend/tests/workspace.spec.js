import { test, expect } from '@playwright/test';
import fs from 'node:fs';

// Small stored ZIP fixture: enough WordprocessingML to test a real multipart upload.
function sampleResume() {
  const content = Buffer.from('<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Avery Parker</w:t></w:r></w:p><w:p><w:r><w:t>Built a C# job tracker using ASP.NET Core and SQLite.</w:t></w:r></w:p></w:body></w:document>');
  const name = Buffer.from('word/document.xml');
  let crc = 0xffffffff;
  for (const byte of content) { crc ^= byte; for (let i=0;i<8;i++) crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0); }
  crc = (crc ^ 0xffffffff) >>> 0;
  const local = Buffer.alloc(30); local.writeUInt32LE(0x04034b50); local.writeUInt16LE(20,4); local.writeUInt32LE(crc,14); local.writeUInt32LE(content.length,18); local.writeUInt32LE(content.length,22); local.writeUInt16LE(name.length,26);
  const central = Buffer.alloc(46); central.writeUInt32LE(0x02014b50); central.writeUInt16LE(20,4); central.writeUInt16LE(20,6); central.writeUInt32LE(crc,16); central.writeUInt32LE(content.length,20); central.writeUInt32LE(content.length,24); central.writeUInt16LE(name.length,28);
  const end = Buffer.alloc(22); end.writeUInt32LE(0x06054b50); end.writeUInt16LE(1,8); end.writeUInt16LE(1,10); end.writeUInt32LE(central.length+name.length,12); end.writeUInt32LE(local.length+name.length+content.length,16);
  return Buffer.concat([local,name,content,central,name,end]);
}
test.describe.configure({ mode: 'serial' });
test.use({ actionTimeout: 15000 });
let userId, jobId, applicationId;
const rowButtons = page => page.locator('.job-list tbody .role-name');
async function submit(page) { await page.locator('#submit-editor').click(); await expect(page.locator('#editor')).not.toBeVisible(); await expect(page.locator('#refresh-button')).not.toHaveClass(/spinning/); }

test('onboarding, job detail, immutable application, notes, and contextual reminder', async ({ page, request }) => {
  await page.goto('/');
  await page.locator('#editor').getByLabel('First name', { exact: true }).fill('Avery');
  await page.locator('#editor').getByLabel('Last name', { exact: true }).fill('Parker');
  await page.locator('#editor').getByLabel('Email address', { exact: true }).fill('avery@example.com');
  await submit(page);
  await expect(page.getByRole('heading', { name: 'Jobs', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Save a job', exact: true }).click();
  await page.locator('#editor').getByLabel('Company', { exact: true }).fill('Microsoft');
  await page.locator('#editor').getByLabel('Job title', { exact: true }).fill('Graduate Software Engineer');
  await page.locator('#editor').getByLabel('Location').fill('Chicago, IL');
  await page.locator('#editor').getByLabel('Job posting link').fill('https://example.com/graduate');
  await page.getByText('Visa information & posting availability', { exact: true }).click();
  await page.locator('#editor').getByLabel('OPT friendly information').fill('Likely; confirm with recruiter');
  await page.locator('#editor').getByLabel('H-1B visa information').fill('Role dependent');
  await page.locator('#editor').getByLabel('Posting availability', { exact: true }).selectOption('Open');
  await submit(page);
  await page.getByRole('button', { name: 'Graduate Software Engineer', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Graduate Software Engineer', exact: true })).toBeVisible();
  await expect(page.getByText('Likely; confirm with recruiter', { exact: true })).toBeVisible();
  await expect(page.locator('#editor').getByLabel('Opportunity', { exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Update application', exact: true }).click();
  await expect(page.locator('#editor').getByLabel('Opportunity', { exact: true })).toHaveCount(0);
  await page.locator('#editor').getByLabel('Application status', { exact: true }).selectOption('Interview');
  await page.locator('#editor').getByLabel('Applied on').fill('2026-10-25');
  await page.locator('#editor').getByLabel('Interview date & time').fill('2030-11-05T10:30');
  await page.locator('#editor').getByLabel('Your notes').fill('Keep these notes with this job.');
  await page.getByText('Tracking & preparation notes', { exact: true }).click();
  await page.locator('#editor').getByLabel('Date found').fill('2026-10-25');
  await page.locator('#editor').getByLabel('Priority', { exact: false }).fill('High — relevant graduate role');
  await page.locator('#editor').getByLabel('Resume tips').fill('Highlight capstone');
  await submit(page);
  await expect(page.getByText('Keep these notes with this job.', { exact: true })).toBeVisible();
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Graduate Software Engineer', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Add reminder', exact: true }).click();
  await page.locator('#editor').getByLabel('What would you like to remember?').fill('Prepare for Microsoft interview');
  await page.locator('#editor').getByLabel('When', { exact: true }).fill('2030-11-04T16:00');
  await submit(page);
  await expect(page.getByRole('button', { name: 'Prepare for Microsoft interview', exact: true })).toBeVisible();
  fs.mkdirSync('../.artifacts/ui',{recursive:true});
  await page.screenshot({path:'../.artifacts/ui/job-details.png',fullPage:true,animations:'disabled'});
  const users = await (await request.get('/api/users')).json(); userId = users[0].id;
  const apps = await (await request.get('/api/applications/user/' + userId)).json();
  applicationId = apps[0].id; jobId = apps[0].jobId;
  const reminders = await (await request.get('/api/reminders/user/' + userId)).json();
  expect(reminders[0].applicationId).toBe(applicationId);
  for (let i=0;i<31;i++) {
    const company = i<28 ? (i%2 ? 'example   labs' : ' Example Labs ') : 'Other Company';
    const job = await (await request.post('/api/jobs', { data:{ company, title:'Graduate role '+i, location:i%2?'Remote':'Austin', postingAvailability:i%3?'Open':'Closed' } })).json();
    const response = await request.post('/api/applications',{data:{userId,jobId:job.id,status:i<28&&i%2?'Applied':'NotApplied',dateFound:i===30?null:i<28?'2026-10-'+String(i+1).padStart(2,'0'):'2026-09-01',priority:i%2?'Medium':'High'}});
    expect(response.ok()).toBeTruthy();
  }
});

test('jobs sort by found date, paginate, filter before paging, and restore list after detail', async ({page})=>{
  await page.goto('/#jobs');
  await expect(rowButtons(page)).toHaveCount(25);
  await expect(rowButtons(page).first()).toHaveText('Graduate role 27');
  await expect(page.getByText('1–25 of 32 jobs',{exact:true})).toBeVisible();
  await page.getByRole('button',{name:'Next',exact:true}).click();
  await expect(rowButtons(page)).toHaveCount(7);
  await expect(rowButtons(page).last()).toHaveText('Graduate role 30');
  await rowButtons(page).first().click();
  await page.getByRole('button',{name:'Back to list',exact:false}).click();
  await expect(page.getByText('Page 2 of 2',{exact:true})).toBeVisible();
  await page.getByRole('searchbox').fill('Graduate role 27');
  await expect(rowButtons(page)).toHaveCount(1);
  await rowButtons(page).first().click();
  await page.getByRole('button',{name:'Back to list',exact:false}).click();
  await expect(page.getByRole('searchbox')).toHaveValue('Graduate role 27');
  await page.reload();
  await expect(rowButtons(page)).toHaveCount(1);
  await page.getByRole('searchbox').fill('');
  await page.getByLabel('Sort jobs',{exact:true}).selectOption('oldest');
  await expect(rowButtons(page).first()).toHaveText('Graduate role 29');
  await page.getByRole('button',{name:'Next',exact:true}).click();
  await expect(rowButtons(page).last()).toHaveText('Graduate role 30');
  await page.getByLabel('Filter by availability',{exact:true}).selectOption('Closed');
  await expect(page.getByText('1–11 of 11 jobs',{exact:true})).toBeVisible();
  fs.mkdirSync('../.artifacts/ui',{recursive:true});
  await page.screenshot({path:'../.artifacts/ui/jobs.png',fullPage:true,animations:'disabled'});
});

test('company grouping, company job context, and source details',async({page})=>{
  await page.goto('/#companies');
  await expect(page.locator('.company-tile')).toHaveCount(3);
  await page.screenshot({path:'../.artifacts/ui/companies.png',fullPage:true,animations:'disabled'});
  await page.getByRole('searchbox').fill('example');
  await expect(page.locator('.company-tile')).toHaveCount(1);
  await page.locator('.company-tile').click();
  await expect(page.getByRole('heading',{name:'Example Labs',exact:true})).toBeVisible();
  await expect(page.getByText('1–25 of 28 jobs',{exact:true})).toBeVisible();
  await expect(rowButtons(page).first()).toHaveText('Graduate role 27');
  await rowButtons(page).first().click();
  await page.getByRole('button',{name:'Back to list',exact:false}).click();
  await expect(page.getByRole('heading',{name:'Example Labs',exact:true})).toBeVisible();
  await page.getByRole('link',{name:'Companies',exact:false}).filter({hasText:'←'}).click();
  await expect(page.getByRole('searchbox')).toHaveValue('example');
});

test('API rejects moving a job or owner and leaves notes and reminder links intact',async({request})=>{
  const original=await (await request.get('/api/applications/'+applicationId)).json();
  const jobs=await (await request.get('/api/jobs')).json();
  for(const body of [{...original,jobId:jobs.find(j=>j.id!==jobId).id},{...original,userId:99999}]){
    const response=await request.put('/api/applications/'+applicationId,{data:body});
    expect(response.status()).toBe(409);
  }
  expect(await (await request.get('/api/applications/'+applicationId)).json()).toEqual(original);
  const reminders=await (await request.get('/api/reminders/user/'+userId)).json();
  expect(reminders[0].applicationId).toBe(applicationId);
});

test('failed writes retain input; applications exclude unsubmitted jobs; profile isolation',async({page})=>{
  await page.goto('/#applications');
  await expect(page.getByText('1–15 of 15 jobs',{exact:true})).toBeVisible();
  await page.getByRole('button',{name:'Graduate Software Engineer',exact:true}).click();
  await page.getByRole('button',{name:'Update application',exact:true}).click();
  await page.locator('#editor').getByLabel('Your notes').fill('Preserve this unsaved text');
  await page.route('**/api/applications/'+applicationId,route=>route.fulfill({status:503,contentType:'application/problem+json',body:JSON.stringify({detail:'User Service temporarily unavailable'})}));
  await page.locator('#submit-editor').click();
  await expect(page.locator('#form-error')).toHaveText('User Service temporarily unavailable');
  await expect(page.locator('#editor').getByLabel('Your notes')).toHaveValue('Preserve this unsaved text');
  await expect(page.locator('#submit-editor')).toBeEnabled();
  await page.keyboard.press('Escape');
  await page.getByRole('button',{name:'Add profile',exact:true}).click();
  await page.locator('#editor').getByLabel('First name',{exact:true}).fill('Jordan');
  await page.locator('#editor').getByLabel('Last name',{exact:true}).fill('Lee');
  await page.locator('#editor').getByLabel('Email address',{exact:true}).fill('jordan@example.com');
  await submit(page);
  await expect(page.getByRole('heading',{name:'Jobs',exact:true})).toBeVisible();
  await page.getByRole('link',{name:'Companies',exact:true}).click();
  await expect(page.locator('.company-tile')).toHaveCount(0);
  await page.getByLabel('Active profile').selectOption({label:'Avery Parker'});
  await expect(page.locator('.company-tile')).toHaveCount(3);
});

test('mobile browsing, contained table scrolling, dialog keyboard and reminder completion',async({browser,baseURL})=>{
  const context=await browser.newContext({baseURL,viewport:{width:390,height:844},reducedMotion:'reduce',isMobile:true,hasTouch:true});
  const page=await context.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.goto('/#jobs');
  await expect(rowButtons(page)).toHaveCount(25);
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
  await rowButtons(page).first().click();
  await page.getByRole('button',{name:'Update application',exact:true}).click();
  await expect(page.locator('#editor').getByLabel('Opportunity',{exact:true})).toHaveCount(0);
  await page.keyboard.press('Escape');
  await expect(page.locator('#editor')).not.toBeVisible();
  await page.getByRole('button',{name:'Open navigation',exact:true}).click();
  await page.getByRole('link',{name:/^Reminders/}).click();
  await page.getByRole('button',{name:'Complete: Prepare for Microsoft interview',exact:true}).click();
  await page.getByRole('button',{name:'Completed',exact:true}).click();
  await expect(page.getByRole('button',{name:'Mark incomplete: Prepare for Microsoft interview',exact:true})).toBeVisible();
  await page.screenshot({path:'../.artifacts/ui/mobile.png',fullPage:true,animations:'disabled'});
  expect(errors).toEqual([]);
  await context.close();
});

test('Google Sheet preview is read-only, escapes source text, and reports unavailable setup', async ({page,request})=>{
  const before=await(await request.get('/api/applications/user/'+userId)).json();
  await page.goto('/#jobs');
  await expect(rowButtons(page)).toHaveCount(25);
  await page.route('**/api/integrations/google-sheets/preview?*',route=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
    sourceRows:530,matchedRows:526,unmatchedRows:0,ambiguousRows:4,rowsWithDifferences:84,
    databaseModified:false,automaticUpdatesEnabled:false,warnings:['Stable IDs need reconciliation'],
    rows:[{rowNumber:2,company:'<img src=x onerror=alert(1)>',title:'Graduate',match:'Matched',warnings:[],changes:[{field:'Notes',databaseValue:'Keep my notes',sheetValue:'Source notes',protected:true}]}]
  })}));
  await page.getByRole('button',{name:'Preview Google Sheet',exact:true}).click();
  await expect(page.locator('#editor').getByText('Connected to Sheet1.',{exact:true})).toBeVisible();
  await expect(page.locator('#editor').getByText('530',{exact:true})).toBeVisible();
  await expect(page.locator('#editor img')).toHaveCount(0);
  await page.locator('#editor summary').filter({hasText:'Row 2:'}).click();
  await expect(page.locator('#editor').getByText('Notes · protected',{exact:true})).toBeVisible();
  await expect(page.locator('#editor button[type="submit"]')).toHaveCount(0);
  await page.locator('#submit-editor').click();
  await page.unroute('**/api/integrations/google-sheets/preview?*');
  await page.getByRole('button',{name:'Preview Google Sheet',exact:true}).click();
  await expect(page.locator('#editor').getByText('Your saved data is unchanged. Close and try again.',{exact:true})).toBeVisible();
  await page.keyboard.press('Escape');
  expect(await(await request.get('/api/applications/user/'+userId)).json()).toEqual(before);
});

test('automatic sync status and Sync now report progress and preserve jobs on failure',async({page,request})=>{
  let failed=false,calls=0;
  const status=()=>({enabled:true,automaticUpdatesEnabled:true,userId,intervalSeconds:300,
    lastSuccessUtc:'2026-10-25T12:00:00Z',created:0,updated:0,skipped:0,warnings:[],
    lastError:failed?'Google is unavailable; the next cycle will retry.':null});
  await page.route('**/api/integrations/google-sheets/status',route=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(status())}));
  await page.route('**/api/integrations/google-sheets/sync',route=>{
    expect(route.request().method()).toBe('POST');calls++;
    return route.fulfill({status:failed?503:200,contentType:'application/json',body:JSON.stringify(failed?{detail:'Saved data is retained. Retry later.'}:{created:0,updated:2,skipped:0,warnings:[]})});
  });
  await page.goto('/#jobs');
  await expect(page.getByRole('region',{name:'Google Sheets sync'})).toContainText('automatic updates on');
  await page.getByRole('button',{name:'Sync now',exact:true}).click();
  await expect(page.getByRole('button',{name:'Sync now',exact:true})).toBeEnabled();
  expect(calls).toBe(1);
  await page.getByRole('button',{name:'Refresh workspace',exact:true}).click();
  await expect(page.locator('#refresh-button')).not.toHaveClass(/spinning/);
  expect(calls).toBe(2);
  failed=true;
  await page.getByRole('button',{name:'Sync now',exact:true}).click();
  await expect(page.getByRole('region',{name:'Google Sheets sync'})).toContainText('Google is unavailable');
  await expect(rowButtons(page)).toHaveCount(25);
  const rows=await(await request.get('/api/applications/user/'+userId)).json();
  expect(rows.length).toBe(32);
});

test('master Word upload, approval, tailoring profiles, setup guidance and profile isolation', async ({page,request}) => {
  await page.goto('/#resumes');
  await expect(page.getByRole('heading',{name:'Resumes',exact:true})).toBeVisible();
  await page.getByLabel('Word document (.docx, up to 2 MB)').setInputFiles({name:'master.docx',mimeType:'application/vnd.openxmlformats-officedocument.wordprocessingml.document',buffer:sampleResume()});
  await page.getByRole('button',{name:'Upload master resume',exact:true}).click();
  const modal=page.locator('#resume-editor');
  await expect(modal.getByRole('heading',{name:'Review master resume'})).toBeVisible();
  await expect(modal.getByLabel('Master resume content')).toContainText('Avery Parker');
  await modal.getByRole('button',{name:'Approve master content'}).click();
  await expect(modal).toHaveCount(0);
  await expect(page.getByRole('button',{name:'View master'})).toBeVisible();
  await page.getByRole('button',{name:'New tailoring profile'}).click();
  await modal.getByLabel('Profile name').fill('Graduate developer');
  await modal.getByRole('button',{name:'Save profile',exact:true}).click();
  await expect(page.getByText('Graduate developer',{exact:true})).toBeVisible();
  const stored=await(await request.get('/api/resumes?userId='+userId)).json();
  expect(stored.masters[0].approved).toBe(true);
  expect(stored.profiles[0].name).toBe('Graduate developer');
  await page.goto('/#job/'+jobId);
  await page.getByRole('button',{name:'Tailor resume',exact:true}).click();
  await expect(modal).toContainText('Configure the AI key and model');
  await modal.getByRole('link',{name:'Open Resumes'}).click();
  const users=await(await request.get('/api/users')).json();
  const other=users.find(u=>u.id!==userId);
  await page.locator('#profile-select').selectOption(String(other.id));
  await expect(page.getByText('No master uploaded yet.')).toBeVisible();
  await expect(page.getByText('Graduate developer',{exact:true})).toHaveCount(0);
  await page.setViewportSize({width:390,height:844});
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
});

test('tailoring UI handles a simulated AI draft, review and Word export',async({page})=>{
  let generated=false,edited=false,exported=false;
  const fakeDraft={id:900,userId,applicationId,jobTitle:'Microsoft · Graduate Software Engineer',status:'Ready',content:'Avery Parker\nBuilt a C# job tracker.',reviewNotes:'Highlight relevant projects. Source line 2.',masterSnapshot:'Avery Parker\nBuilt a C# job tracker.',jobDescription:'Example job description',profileSnapshot:'Example preferences',createdAt:'2026-10-25T12:00:00Z'};
  await page.route('**/api/resumes/settings',r=>r.fulfill({json:{configured:true,model:'fake-test-model'}}));
  await page.route('**/api/resumes?userId=*',async r=>{
    const actual=await r.fetch(); const data=await actual.json();
    if(generated)data.drafts.unshift(fakeDraft);
    if(exported)data.documents.unshift({id:900,draftId:900,submitted:false});
    await r.fulfill({json:data});
  });
  await page.route('**/api/resumes/drafts',async r=>{expect(r.request().postDataJSON().applicationId).toBe(applicationId);generated=true;await r.fulfill({status:202,json:{id:900,status:'Queued'}});});
  await page.route('**/api/resumes/drafts/900?*',async r=>{
    if(r.request().method()==='PUT'){edited=true;await r.fulfill({status:204});}
    else await r.fulfill({json:fakeDraft});
  });
  await page.route('**/api/resumes/drafts/900/export?*',async r=>{exported=true;await r.fulfill({json:{id:900}});});
  await page.goto('/#job/'+jobId);
  await page.getByRole('button',{name:'Tailor resume',exact:true}).click();
  const modal=page.locator('#resume-editor');
  await modal.getByLabel('Full job description').fill('We are looking for a graduate software engineer with C# and ASP.NET Core experience, interested in building useful web applications.');
  await modal.getByRole('button',{name:'Generate draft',exact:true}).click();
  await page.getByRole('button',{name:'Open draft',exact:true}).click();
  await modal.getByLabel('Resume content', {exact:true}).fill('Avery Parker\nDeveloped a C# job tracker.');
  await modal.getByRole('checkbox').check();
  await modal.getByRole('button',{name:'Save & export Word',exact:true}).click();
  await expect(modal.getByRole('link',{name:'Download Word resume'})).toBeVisible();
  expect(edited&&exported).toBe(true);
});
