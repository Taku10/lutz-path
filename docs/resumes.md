# Resume tailoring

Open **Resumes** in the sidebar. Select your usual LutzPath profile first.

1. Choose **Upload master resume** and select a `.docx` file (up to 2 MB).
2. Review the extracted text, then approve it. Keep one line per original paragraph: do not add, delete or reorder paragraphs. Correct complex layouts, mixed formatting or hyperlinks in Word and upload again. Approval freezes the version; previous versions remain available.
3. Create a tailoring profile with writing instructions and a page target. Your uploaded Word file controls fonts and layout. You can save several profiles for different types of roles.
4. Open a tracked job and choose **Tailor resume**. Select an approved master and profile, and paste the full job description. A job link alone is not enough.
5. Generate a draft. Its state appears in Resumes and updates while generation is running.
6. Open the ready draft, review the changes and source references, and edit the wording within individual paragraphs. Protected paragraphs stay unchanged. Confirm your review, then save and export Word.
7. Download the saved Word version. Check pagination in Word. Mark that specific version submitted when you use it in an application; this does not submit an application for you.

## AI configuration

Master uploads and tailoring profiles work without an AI account. Generation uses the OpenAI Responses API with structured output. A ChatGPT subscription does not supply API billing or an API key.

In your ignored `.env` file, configure:

```dotenv
RESUME_AI_MODEL=your-structured-output-capable-model
RESUME_AI_API_KEY=your-api-key
```

Choose a model supported by your API account that supports structured outputs. Keep the real key out of source control and browser code. Restart the Resume Service after configuration:

```powershell
docker compose -f docker-compose.yml -f docker-compose.sheets.yml up --build --detach resumeservice frontend
```

Use the Google Sheets override when it is already configured. For a fresh installation without Sheets, use only `docker-compose.yml`. Generation sends the selected master text, instructions and job description to OpenAI, and incurs API charges. The request sets `store: false`; provider data handling still follows your API account's terms.

Alternatively, configure `AI__ApiKeyFile` on the service with a mounted secret file instead of the API-key environment variable. Do not commit a key file.

## Data and behavior

- The fourth service owns `/data/resumes.db` and `/data/documents/` on the `resumes-data` named volume. Original uploads and generated documents have random storage names; downloads use database IDs filtered by the selected user.
- Each generation stores the approved master text, profile settings, job description, application ID and model. Changing a master or creating another profile does not change existing drafts.
- Source-line references help review the generated content; they cannot establish that the model's rephrasing is factually correct. You must check every claim. References refer to the initial AI draft and may no longer align after manual edits.
- New generations use the original uploaded DOCX as the template. Export changes only text in safe, uniformly formatted paragraphs, retaining paragraph/run properties and the rest of the Word package (styles, numbering, margins, tables, headers, footers, images, relationships). Paragraphs with mixed run formatting, hyperlinks, tabs, fields, content controls or other complex content are protected and remain unchanged. The original upload is never overwritten.
- AI returns paragraph IDs and replacements using the application's enforced schema. Custom instructions can guide wording but cannot switch the JSON shape, add sections, reorder paragraphs or change formatting. Put factual information in the approved master, rather than relying on facts in a prompt. Remove custom JSON-output examples from older prompts to avoid conflicting instructions.
- If an older master was approved after adding/deleting lines, generation refuses to guess its mapping. Upload the corrected Word file and approve it again. Previously generated drafts without a template link retain their old plain-layout export behavior; generate a new draft for template-preserving export. Previously saved files stay unchanged.
- The browser displays content, not an exact Word preview. Longer replacements can change wrapping and pagination despite preserving formatting. Check page breaks in Word; a one-page target is not a guarantee.
- Every export saves a new immutable Word file. Downloads never call AI, and editing a draft never alters a previously exported or submitted file.
- Generations are queued in SQLite and processed by a single worker. Repeated requests with the same request ID return the same draft. Failed requests are not automatically retried. A restart marks an in-progress generation failed; queued requests continue. An interrupted provider request might already have incurred a charge.
- Run one Resume Service instance with this SQLite volume. Do not scale replicas against it.
- Services validate users and application ownership over HTTP; they do not read another service's database. Dependency failures return a helpful error without starting generation.
- Like the rest of this learning app, profile selection is not authentication. Keep the app on localhost until real authentication and authorization are added.

## API overview

All personal-resource endpoints take `?userId=...` except draft creation, which receives `userId` in its JSON body.

| Endpoint | Purpose |
| --- | --- |
| `GET /api/resumes/settings` | Whether server AI configuration is present; no keys returned |
| `GET /api/resumes` | Master's versions, profiles, drafts and exported files |
| `POST /api/resumes/masters` | Multipart Word upload (`file`) |
| `POST /api/resumes/masters/{id}/approve` | Approve reviewed content |
| `GET /api/resumes/masters/{id}/download` | Download original upload |
| `POST /api/resumes/profiles` | Save a tailoring profile |
| `PUT /api/resumes/profiles/{id}` | Edit preferences without changing existing draft snapshots |
| `POST /api/resumes/drafts` | Validate and enqueue generation |
| `GET /api/resumes/drafts/{id}` | Generation status, content and snapshots |
| `PUT /api/resumes/drafts/{id}` | Save edits to a ready draft |
| `POST /api/resumes/drafts/{id}/export` | Save a new Word document version |
| `GET /api/resumes/documents/{id}/download` | Download saved Word version |
| `POST /api/resumes/documents/{id}/submitted` | Mark that exported version submitted |

No scraping, automatic application submission, or resume data syncing back to Google Sheets is performed.
