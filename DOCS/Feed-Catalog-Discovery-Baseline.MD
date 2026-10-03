# Feed Catalog Discovery Baseline

## Status

Phase 0 setup is ready. The repository and screenshot inventory below is populated. No participant sessions have been run yet, so usability timings, completion rates, user quotes, and target thresholds remain **pending**. Do not treat repository inspection as user research.

## Observed Baseline

**User-provided screenshot** (capture date not recorded):

- The catalog header reports **575 feeds**.
- Feeds appear in one long, vertically scrolling list.
- Each row shows a name, the repeated category label `Comics RSS`, a feed URL, and a Follow/Unfollow button.
- There is no visible search box, category navigation, or hide-followed filter.
- At least two rows are named `9 Chickweed Lane by Brooke McEldowney`; the URLs distinguish their destinations.

**Checked-in seed file:** [`ComicsRSS.opml`](../ComicsRSS.opml), dated October 3, 2026 in its OPML metadata.

- 569 feed entries, which is six fewer than the screenshot's 575. The screenshot and seed file are different snapshots; use one fixed catalog snapshot for any comparable test.
- One top-level category: `Comics RSS`.
- 0/569 entries have a description; 569/569 have an `htmlUrl`.
- All 569 feed endpoints use `www.comicsrss.com`; the `htmlUrl` destinations are `www.gocomics.com` (429), `comicskingdom.com` (96), `www.arcamax.com` (43), and `dilbert.com` (1).
- One duplicate display-name group is present: `9 Chickweed Lane by Brooke McEldowney` appears twice, with different feed URLs and publisher destinations.

**Static quality audit:** Parsed the checked-in OPML as XML on October 3, 2026. Feed URLs were validated as absolute HTTP(S) URIs and grouped by normalized `System.Uri.AbsoluteUri`; destination URLs were checked the same way. These results apply to the seed file only.

- 0/569 entries have a blank name.
- 569/569 feed URLs are valid HTTPS URLs; 0 normalized duplicate endpoint URLs.
- 569/569 entries have a blank description.
- 569/569 entries have a valid HTTP(S) `htmlUrl` destination.
- 1 duplicate display-name group (2 entries); both entries have distinct destinations.
- 1 root category contains all 569 entries.

**Observed in code:** the browser loads all catalog feeds into one collection and displays category as row metadata. Following an individual feed invokes a folder-selection flow. The catalog feed shape has name, URL, optional description, and one optional category; it does not currently store creator or health metadata.

### Baseline Limitations

- The live catalog state behind the screenshot was not available as a checked-in data file, so the 575 entries have not been independently counted or audited.
- OPML metadata is useful for initial discovery but is not enough to assign trustworthy subcategories or descriptions.
- The domain counts describe this seed snapshot only. Re-run the inventory against the exact snapshot used for later tests.
- No feed endpoint was fetched during this inventory. Feed freshness, availability, response quality, and duplicate normalized URLs are not measured.

## Moderator Protocol

**Participants:** Recruit 5-8 people for qualitative discovery, including people who are new to RSS and people who already subscribe to feeds. This sample is for finding usability issues, not for statistical claims.

**Session length:** 20-25 minutes per participant.

**Environment:** Use the same app build, catalog snapshot, display size, and profile setup for all baseline sessions. Use a disposable regular profile with a `Comics` folder and no subscriptions to the test feeds. Disable automatic refresh/network-dependent behavior during sessions; use the same deterministic local data for every participant. Reset the profile between participants.

**Privacy:** Ask permission before recording. Prefer anonymous participant IDs and written observations. Do not collect names, email addresses, or unrelated browsing history.

**Opening script:**

> We are evaluating how easy the feed catalog is to use, not testing you. Please say what you are looking for and what you expect as you go. You can stop or change your mind at any time. I will avoid helping unless you ask.

Do not point out search, category, or duplicate-name issues before the tasks. If a participant asks for help, provide the minimum help needed and record what was asked and provided.

### Tasks

1. **Known-feed task:** "You want to follow 9 Chickweed Lane from GoComics. Find that feed and follow it into your Comics folder."
   - Start with both same-name entries present and neither followed.
   - Success requires following the GoComics destination, not merely selecting either matching title.
   - Record wrong-variant selection even if the participant corrects it.

2. **Open discovery task:** "Browse the catalog and find one comic you would consider following. Tell me what helped you decide, then follow it into your Comics folder."
   - There is no objectively correct feed. Record the feed chosen and the evidence the participant used (name, creator, destination, description, category, or other).
   - Ask for confidence from 1 (guessing) to 5 (very confident) after the choice.

3. **Publisher-information task:** "Choose a feed that you think is from somewhere other than GoComics. Using only the information visible in the catalog, tell me which publisher or site it goes to if you can. If you cannot tell, say what is missing."
   - This is an information-audit task, not a user pass/fail test. Do not let the participant open external sites. Record whether the current row supports a confident answer and what information the participant expected to see.

Stop a task after five minutes or when the participant abandons it. Do not coach during the timed task. Ask retrospective questions only after timing is stopped.

**After-task questions:**

- What was hardest to find or understand?
- Was any feed information missing that would have changed your decision?
- Did any two entries look like the same feed? How did you distinguish them?
- What would you expect to happen after choosing Follow?
- Which task felt least confident, and why?

## Scorecard

Record one row per participant and task. Use anonymous IDs (for example, P01). Use `N/A` when an item does not apply; do not infer success from time alone.

| Participant | Task | Completed correctly? | Time (seconds) | Wrong feed/attempts | Asked for help? | Confidence (1-5) | Notes / exact quote |
| --- | --- | ---: | ---: | --- | --- | ---: | --- |
| P__ | 1 | | | | | | |
| P__ | 2 | | | | | | |
| P__ | 3 | | | | | | |

For each task, capture:

- Start/end event used for timing (start when the task is read; stop at correct selection, abandonment, or five-minute limit).
- Whether the intended feed was selected, not just whether a Follow action occurred.
- Navigation path, backtracking, repeated scrolling, and any wrong selection.
- Assistance requested, visible state misunderstood, or missing information mentioned.
- One short observation or quote that explains the behavior; avoid collecting personal details.

### Summary Template

Fill this in after sessions. Keep individual participant data anonymous.

- Sessions completed: `__ / 5-8`
- Catalog snapshot/build: `__`
- Task 1 correct completions: `__ / __`; median time: `__ sec`; wrong-variant selections: `__`
- Task 2 completions: `__ / __`; median time: `__ sec`; median confidence: `__ / 5`
- Task 3 correct publisher identifications: `__ / __`; median time: `__ sec`
- Most frequent friction points: `__`
- Most useful missing metadata, as requested by participants: `__`
- Findings to address in Phase 1: `__`
- Open questions for catalog curator: `__`
- Numeric success targets for the Phase 1 comparison: `__`

Do not publish averages or percentages without the sample size. With this small sample, report counts, medians, and recurring observations, and label findings as qualitative.

## Taxonomy and Ownership Worksheet

Use the following draft definitions to guide a decision with the catalog owner; confirm or revise them before adding categories:

- **Category:** a broad, maintained catalog classification used to browse feeds. The current feed model supports one category per feed, so keep categories understandable and mutually manageable.
- **Collection:** a curated, non-exclusive set of catalog feeds organized around an editorial purpose or user need. A feed may belong to multiple collections. Collections are not personal folders.
- **Personal folder:** a profile-owned organization choice made when a user follows a feed. It must not change the shared catalog's category or collection membership.
- **Publisher/site:** identifying metadata or a filter facet, not a category. In the seed data, the feed URL host is the aggregator (`www.comicsrss.com`) while `htmlUrl` provides the destination site.
- **Tags:** optional cross-cutting descriptors only if there is a clear owner and maintenance process. Do not create tags just to simulate subcategories without a consistent taxonomy.

**Initial recommendation:** keep `Comics RSS` as the sole broad category until a curator can classify entries consistently. Do not infer genres or formats from feed titles alone. Validate whether users want narrower categories (for example, editorial cartoons, serialized stories, or vintage strips) before adding them, and prefer curated collections when a grouping is intentionally selective or non-exclusive.

| Decision | Proposed default | Owner / decision | Status |
| --- | --- | --- | --- |
| Category semantics | Broad primary classification; one category per feed | Catalog owner | Pending |
| Collections semantics | Curated, non-exclusive sets; distinct from categories and personal folders | Catalog owner | Pending |
| `Comics RSS` label | Keep for baseline; decide whether user-facing label should be `Comics` | Product + catalog owner | Pending |
| Subcategories | Add only after curator review and user validation | Product + catalog owner | Pending |
| Publisher identity | Use verified `htmlUrl` destination to distinguish sources; do not treat feed endpoint host as publisher | Catalog owner | Draft |
| Catalog metadata ownership | Shared fields edited by Catalog Master; subscriptions/folders remain profile-owned | Product | Draft |

## Catalog Quality Checklist

Run against the fixed snapshot used for usability sessions and again before launch. Record counts and examples; do not silently modify feed data as part of measurement.

- Every feed has a non-empty display name and a valid HTTP(S) feed URL.
- Normalized feed URLs are unique, or duplicate endpoints are explicitly reviewed.
- Same-name entries have enough verified context (publisher/site, creator, or edition) to distinguish them in the browser.
- Category assignments use agreed taxonomy definitions; missing assignments are visible to catalog maintainers.
- Descriptions are accurate, concise, and sourced. The seed OPML has none, so the UI must not reserve blank description space or fabricate copy.
- Publisher/site destinations are valid HTTP(S) URLs where present. The seed file provides `htmlUrl` on every entry; validate before displaying it as trusted context.
- Feed availability and freshness are checked separately from metadata quality. Any health result records the checked time and failure reason; one failure must not delete a feed or its cached articles.
- Collections have an owner, a short purpose, and reviewed membership. Do not use a collection as a substitute for an undefined category.

| Check | Snapshot count / result | Examples / follow-up |
| --- | --- | --- |
| Total feed entries | Screenshot: 575; checked-in OPML: 569 | Reconcile snapshot before comparison |
| Missing names | OPML: 0 / 569 | |
| Invalid or duplicate normalized feed URLs | OPML: 0 invalid; 0 duplicate normalized URLs | All 569 are valid HTTPS URLs |
| Duplicate display-name groups | OPML: 1 (`9 Chickweed Lane by Brooke McEldowney`) | Distinct publisher destinations |
| Missing descriptions | OPML: 569 / 569 | Do not invent descriptions |
| Missing/invalid publisher destinations | OPML: 0 / 569 | All have a valid HTTP(S) `htmlUrl` |
| Category coverage | OPML: 569 / 569 in `Comics RSS` | No finer classifications in seed |
| Feed health/freshness | Not measured | Requires a separate bounded check |

## Phase 0 Completion Gate

Phase 0 is complete when:

- The exact catalog snapshot and build for the baseline are recorded.
- At least five moderated sessions are summarized with anonymous task-level scorecards, including observed problems and no fabricated or network-derived assumptions.
- Category, collection, publisher, and personal-folder semantics are accepted by the product/catalog owner, or listed as explicit blockers for Phase 1.
- Catalog quality checks are run against the chosen snapshot, with unresolved issues assigned an owner.
- Phase 1 success targets are set from observed baseline results and product priorities.

Until those items are complete, the current document is a **baseline setup and repository inventory**, not a completed usability study.
