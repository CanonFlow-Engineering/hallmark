"use strict";
// Days are estimates. A null duration has no assigned window.
const deliveryPlan = {
    updated: "24 Nov 2026",
    forgejo: "http://localhost:3000/arun/hallmark",
    tasks: [
        {
            id: "HM-01", phase: "pilot", status: "complete", days: 1, after: [],
            title: "Lock the Hallmark CLI (Rust) and Lifecycle Engine (F#) split",
            refs: "CF-067, CF-069, CF-078",
            detail: "Give Hallmark CLI (Rust) operator commands, pinned worker launch and bundle verification, and Hallmark Lifecycle Engine (F#) planning, execution, run journal, recovery and release admission.",
            acceptance: "CF-067, docs/ARCHITECTURE.md and ROADMAP.md define the formal Hallmark CLI (Rust) and Hallmark Lifecycle Engine (F#) ownership boundary.",
            evidence: "Formal ownership table and decisions D0–D4 recorded in docs/ARCHITECTURE.md and CF-067."
        },
        {
            id: "HM-02", phase: "pilot", status: "complete", days: 1, after: ["HM-01"],
            title: "Qualify the .NET 10 Hallmark Lifecycle Engine (F#) with FsAssay",
            refs: "CF-038, CF-053, CF-065, CF-067",
            detail: "Restore the five F# projects on .NET 10 and emit schema-valid TRX test reports for FsAssay verification.",
            acceptance: "Locked restore, Release build and registered TRX test execution pass under FsAssay 1.0.4 verify.",
            evidence: "FsAssay 1.0.4 verify returned authoritative Pass (5/5 F# projects loaded, 16/16 files analyzed, 61/61 TRX tests passed)."
        },
        {
            id: "HM-03", phase: "pilot", status: "complete", days: 2, after: ["HM-02"],
            title: "Verify the Rust recovery and SOTA lifecycle core",
            refs: "CF-038, CF-053, CF-067, CF-074",
            detail: "Maintain the pure #![no_std] Rust kernel covering recovery, six-stage DAG invariants, Gate B semilattice and Bundle v2 checks.",
            acceptance: "Exercise applied, absent and unknown ledger outcomes, DAG release ancestor coverage and target fingerprint checks.",
            evidence: "14/14 Rust workspace tests, Clippy (-D warnings) and RustAssay Pass (5/5 core files, 0 findings) verified."
        },
        {
            id: "HM-04", phase: "pilot", status: "complete", days: 4, after: ["HM-03"],
            title: "Persist local runs and deterministic Bundle v2 archives",
            refs: "CF-002, CF-005, CF-038–CF-040",
            detail: "Save the frozen plan and pending operation under the state lock and normalize archive entry timestamps for deterministic bundles.",
            acceptance: "Repeated bundle creation produces identical byte-for-byte SHA-256 digests and target admission rejects cross-environment replay.",
            evidence: "Pinned archive timestamps to 1980-01-01T00:00:00Z, sorted artifact digests and verified target fingerprint admission."
        },
        {
            id: "HM-05", phase: "pilot", status: "complete", days: 4, after: ["HM-04"],
            title: "Enforce Migration Ledger v2 and catalog digest audit",
            refs: "CF-002–CF-005, CF-038, CF-046",
            detail: "Audit applied script ordering, checksum equality, ExpectedCatalogDigest match, live pg_catalog drift and active sessions.",
            acceptance: "Reject checksum drift, expected-vs-observed catalog mismatch, live post-migration catalog drift and active non-idle sessions.",
            evidence: "Admission.auditMigrationLedgerV2 verifies ExpectedCatalogDigest, live catalog digest and active session count."
        },
        {
            id: "HM-06", phase: "pilot", status: "complete", days: 4, after: ["HM-05"],
            title: "Wire Hallmark CLI (Rust) to the pinned rootless Podman F# worker",
            refs: "CF-001, CF-005, CF-039, CF-069, CF-075",
            detail: "Enforce sha256 worker image digests, rootless Podman checks, keep-id UID mapping and automatic host manifest/state bind mounts.",
            acceptance: "Hallmark CLI (Rust) plans host manifests through the pinned rootless Podman worker and passes clean-checkout delegation tests.",
            evidence: "Verified host-manifest planning via HALLMARK_WORKER_IMAGE in rootless Podman and clean-checkout Cargo delegation tests."
        },
        {
            id: "HM-07", phase: "pilot", status: "complete", days: 4, after: ["HM-06"],
            title: "Enforce ancestor-scoped Gate B bound-receipt admission in SealRelease",
            issue: 1,
            refs: "CF-028, CF-053, CF-074, CF-075, CF-078",
            detail: "Scope SealRelease to transitive Verify ancestors of Release and verify Assay receipts bound to plan, source, policy and target digests.",
            acceptance: "Unrelated Verify branches do not block Release, while missing, failed or mismatched bound receipts refuse release signing.",
            evidence: "Runner.fs evaluates Admission.evaluateBoundReleaseGate over transitive Verify ancestors before Storage.seal."
        },
        {
            id: "HM-15", phase: "pilot", status: "complete", days: 4, after: ["HM-07"],
            title: "Unify the six-stage Hallmark Lifecycle Engine (F#) and target-aware recovery",
            refs: "CF-001–CF-005, CF-028, CF-067, CF-083–CF-084",
            detail: "Execute the six-stage graph, ExternalLedger readback reconciliation, fsync checkpoint durability and hallmark { ... } CE DSL.",
            acceptance: "All 61 F# unit and runtime tests pass on multi-branch release, ExternalLedger single-dispatch recovery and bundle verification.",
            evidence: "Verified 61/61 F# tests in Hallmark.Tests via VSTest and TrxRunner.fs under FsAssay 1.0.4 verify."
        },
        {
            id: "HM-08", phase: "pilot", status: "complete", days: 2, after: ["HM-07", "HM-15"],
            title: "Qualify the clean-host Wallet recovery and release drill",
            refs: "CF-038, CF-065, CF-067, CF-075, CF-078",
            detail: "Run the six-stage Wallet upgrade through the first-class F# postgres-migrate adapter, refuse release on missing or mismatched receipts, recover an interrupted migration with one dispatch and verify the signed bundle.",
            acceptance: "An operator can launch the pinned worker container, recover without repeating a committed migration and verify the signed bundle from independent Rust and container consumers.",
            evidence: "scripts/verify-wallet-engine.sh passed on rootless Podman PostgreSQL 18.7 and Flyway 13.12.1 (1 Flyway dispatch, missing/mismatched Gate B receipts refused, bundle verified natively in Rust and in an isolated container)."
        },
        {
            id: "HM-09", phase: "mvp", status: "complete", days: 5, after: ["HM-08"],
            title: "Qualify F# workflow DSL to JSON and local stdio MCP",
            refs: "CF-020, CF-069–CF-072, CF-083–CF-084, CF-086",
            detail: "Expose pure F# hallmarkWorkflow { ... } workflow emission to canonical JSON and a local stdio MCP server over the shared planner and Gate B reducer.",
            acceptance: "Equivalent F# DSL and JSON declarations produce the same frozen plan digest, and an MCP client observes identical Gate B decisions.",
            evidence: "Wire.toJson + hallmarkWorkflow CE DSL and runtime/Mcp.fs (hallmark mcp) verified in RuntimeTests.fs under FsAssay 1.0.4 verify (64/64 TRX tests passed)."
        },
        {
            id: "HM-10", phase: "mvp", status: "complete", days: 6, after: ["HM-09"],
            title: "Build the three-owner release fixture",
            refs: "CF-043–CF-045, CF-075, CF-077–CF-078, CF-090",
            detail: "Exercise distinct component owners, mixed-version evidence and VERA supersession/withdrawal refusal across multi-service dependencies.",
            acceptance: "Cutover stays held while compatibility evidence is unresolved. Downstream consumers receive the relevant note and later correction.",
            evidence: "Admission.evaluateThreeOwnerCutover and examples/three-owner/fixture.json verified across CLI (three-owner-check), MCP (hallmark_three_owner_evaluate) and FsAssay 1.0.4 verify (66/66 TRX tests passed)."
        },
        {
            id: "HM-11", phase: "mvp", status: "complete", days: 1, after: ["HM-10"],
            title: "Observe an independent operator",
            refs: "CF-045, CF-065, CF-086, CF-090",
            detail: "Provide single-command CLI/MCP operator triage (`hallmark engine triage` / `hallmark_triage`) and run a cold-start operator observation on the mid-incident workspace.",
            acceptance: "Record how long it takes to identify the committed effect, find the blocker owner and choose a safe next action.",
            evidence: "scripts/verify-operator-drill.sh, hallmark engine triage, and cold-start isolated operator observation identified committed V2 migration, opensearch-projection-team blocker (VERA-CORR-2026-09), and safe resume action (67/67 TRX tests passed; external human operator study noted in QUALIFICATION.md)."
        },
        {
            id: "HM-12", phase: "later", status: "later", days: null, after: ["HM-11"],
            title: "Add the release inspection page",
            refs: "CF-087–CF-092, CF-094",
            detail: "Start with a read-only release page, attention queue, timeline and evidence inspection. Use the pilot to decide what the operator needs next.",
            acceptance: "The operator can follow a blocker to its original receipt with the same identity and status as CLI and MCP."
        },
        {
            id: "HM-13", phase: "later", status: "later", days: null, after: ["HM-11"],
            title: "Qualify a second unchanged consumer",
            refs: "CF-073–CF-076, CF-079–CF-081",
            detail: "Use Orders to test reuse of the shared contracts. Choose additional languages and targets from observed need.",
            acceptance: "An installed consumer exercises its own pinned runtime, upgrade path and configured gates."
        },
        {
            id: "HM-14", phase: "later", status: "later", days: null, after: ["HM-12"],
            title: "Add scheduling and operational charts",
            refs: "CF-091–CF-094",
            detail: "Qualify Gantt, additional Mermaid views and aggregate metrics against actual timing and run data.",
            acceptance: "Forecasts cite their inputs and every count drills down to the same scoped records."
        }
    ]
};
function schedulePlan(taskList) {
    return taskList.reduce((schedule, task) => {
        if (schedule.kind === "invalid" || task.days === null)
            return schedule;
        const dependencies = task.after.map((id) => ({
            id,
            range: schedule.entries.find((entry) => entry.id === id)?.range
        }));
        const missing = dependencies.find((dependency) => dependency.range === undefined);
        if (missing) {
            return { kind: "invalid", taskId: task.id, dependencyId: missing.id };
        }
        const start = 1 + Math.max(0, ...dependencies.map((dependency) => dependency.range?.end ?? 0));
        const range = { start, end: start + task.days - 1 };
        return { kind: "scheduled", entries: [...schedule.entries, { id: task.id, range }] };
    }, { kind: "scheduled", entries: [] });
}
function taskStatus(task, taskList) {
    if (task.status !== "queued")
        return task.status;
    const ready = task.after.every((id) => taskList.some((predecessor) => predecessor.id === id && predecessor.status === "complete"));
    return ready ? "ready" : "waiting";
}
function searchableTask(task) {
    const requirementIds = Array.from(task.refs.matchAll(/CF-(\d{3})(?:–CF-(\d{3}))?/g))
        .flatMap(([, first, last]) => {
        const start = Number(first);
        const end = Number(last ?? first);
        return Array.from({ length: end - start + 1 }, (_, index) => `CF-${String(start + index).padStart(3, "0")}`);
    });
    return `${task.id} ${task.title} ${requirementIds.join(" ")} ${task.detail}`.toLowerCase();
}
const tasks = deliveryPlan.tasks;
const byId = new Map(tasks.map((task) => [task.id, task]));
const viewKind = document.body.dataset.view;
if (viewKind !== "gantt" && viewKind !== "kanban")
    throw new Error("Unknown delivery view");
const phaseNames = {
    pilot: "Wallet pilot", mvp: "Public MVP", later: "After the pilot"
};
const laneNames = {
    ready: "Ready", active: "In progress", blocked: "Blocked",
    waiting: "Waiting", complete: "Complete", later: "Later"
};
const planSchedule = schedulePlan(tasks);
if (planSchedule.kind === "invalid") {
    throw new Error(`Task ${planSchedule.taskId} needs an earlier scheduled dependency ${planSchedule.dependencyId}`);
}
const scheduled = new Map(planSchedule.entries.map((entry) => [entry.id, entry.range]));
function taskById(id) {
    const task = byId.get(id);
    if (!task)
        throw new Error(`Unknown dependency ${id}`);
    return task;
}
function scheduleFor(id) {
    const range = scheduled.get(id);
    if (!range)
        throw new Error(`Scheduled work requires an earlier scheduled dependency ${id}`);
    return range;
}
function element(tag, className = "", text = "") {
    const node = document.createElement(tag);
    if (className)
        node.className = className;
    if (text)
        node.textContent = text;
    return node;
}
function requiredElement(selector, kind) {
    const node = document.querySelector(selector);
    if (!(node instanceof kind))
        throw new Error(`Missing or invalid element ${selector}`);
    return node;
}
const themeStorageKey = "hallmark-delivery-theme";
const themeChoice = requiredElement("#theme", HTMLSelectElement);
function parseTheme(value) {
    return value === "system" || value === "light" || value === "dark" ? value : undefined;
}
function initialTheme() {
    const linked = parseTheme(new URLSearchParams(location.search).get("theme"));
    if (linked)
        return linked;
    try {
        return parseTheme(localStorage.getItem(themeStorageKey)) ?? "system";
    }
    catch {
        return "system";
    }
}
function themeUrl(href, theme) {
    const url = new URL(href, location.href);
    url.searchParams.set("theme", theme);
    return url.href;
}
function applyTheme(theme) {
    document.documentElement.dataset.theme = theme;
    themeChoice.value = theme;
    document.querySelectorAll("a[data-view-link]").forEach((link) => {
        link.href = themeUrl(link.href, theme);
    });
    try {
        localStorage.setItem(themeStorageKey, theme);
    }
    catch {
        // Links carry the preference when storage is unavailable for local files.
    }
}
function timing(task) {
    const range = scheduled.get(task.id);
    if (!range)
        return "Unscheduled";
    return range.start === range.end ? `Day ${range.start}` : `Days ${range.start}–${range.end}`;
}
function taskButton(task, className = "task-link", text = task.title) {
    const button = element("button", className, text);
    button.type = "button";
    button.dataset.task = task.id;
    button.addEventListener("click", () => openTask(task));
    return button;
}
function issueLink(task) {
    if (task.issue === undefined)
        return;
    const link = element("a", "issue-link", `Forgejo issue #${task.issue}`);
    link.href = `${deliveryPlan.forgejo}/issues/${task.issue}`;
    return link;
}
const dialog = requiredElement("#task-dialog", HTMLDialogElement);
const detail = requiredElement("#task-detail", HTMLDivElement);
function addDetail(list, label, value) {
    list.append(element("dt", "", label), element("dd", "", value));
}
function openTask(task) {
    requiredElement("#task-title", HTMLHeadingElement).textContent = task.title;
    requiredElement("#task-meta", HTMLParagraphElement).textContent =
        `${task.id} · ${phaseNames[task.phase]} · ${laneNames[taskStatus(task, tasks)]} · ${timing(task)}`;
    const fields = element("dl");
    if (task.after.length) {
        addDetail(fields, "Depends on", task.after.map((id) => taskById(id).title).join(", "));
    }
    addDetail(fields, "Acceptance check", task.acceptance);
    addDetail(fields, "Requirements", task.refs);
    if (task.evidence)
        addDetail(fields, "Observed evidence", task.evidence);
    const issue = issueLink(task);
    if (issue) {
        const link = element("dd");
        link.append(issue);
        fields.append(element("dt", "", "Tracking issue"), link);
    }
    const otherView = viewKind === "gantt" ? "Kanban" : "Gantt";
    const alternative = element("a", "alternate", `Open in ${otherView}`);
    const taskPath = `${otherView.toLowerCase()}.html?task=${encodeURIComponent(task.id)}`;
    alternative.href = themeUrl(taskPath, parseTheme(themeChoice.value) ?? "system");
    detail.replaceChildren(element("p", "", task.detail), fields, alternative);
    dialog.showModal();
}
function renderSummary() {
    const pilot = tasks.filter((task) => task.phase === "pilot");
    const complete = pilot.filter((task) => task.status === "complete").length;
    const lastPilot = pilot.at(-1);
    const pilotFinish = lastPilot && scheduled.get(lastPilot.id)?.end;
    const indicators = [
        [`${complete} of ${pilot.length}`, "Pilot tasks complete"],
        [pilotFinish ? `Day ${pilotFinish}` : "Unscheduled", "Estimated pilot finish"],
        [deliveryPlan.updated, "Plan updated"]
    ];
    const summary = requiredElement("#summary", HTMLDivElement);
    for (const [value, label] of indicators) {
        const box = element("div", "metric");
        box.append(element("strong", "", value), element("span", "", label));
        summary.append(box);
    }
    const next = tasks.find((task) => taskStatus(task, tasks) === "ready");
    const panel = requiredElement("#next-task", HTMLElement);
    panel.append(element("h2", "", "Next ready task"));
    if (next) {
        panel.append(taskButton(next), element("p", "muted", next.detail));
        const issue = issueLink(next);
        if (issue) {
            const tracking = element("p");
            tracking.append(issue);
            panel.append(tracking);
        }
    }
    else {
        panel.append(element("p", "", "No task is ready."));
    }
}
function card(task) {
    const item = element("li", "card");
    item.dataset.task = task.id;
    item.append(element("span", "card-meta", `${task.id} · ${phaseNames[task.phase]} · ${timing(task)}`), taskButton(task), element("p", "", task.detail));
    return item;
}
function backlog(items, title) {
    const section = element("section", "backlog");
    section.append(element("h2", "", title));
    const list = element("ul");
    list.append(...items.map(card));
    section.append(list);
    return section;
}
function board(visible) {
    const result = document.createDocumentFragment();
    const grid = element("div", "board");
    const lanes = ["ready", "active", "waiting", "complete"];
    if (visible.some((task) => taskStatus(task, tasks) === "blocked"))
        lanes.splice(2, 0, "blocked");
    for (const name of lanes) {
        const lane = element("section", "lane");
        lane.dataset.status = name;
        const items = visible.filter((task) => taskStatus(task, tasks) === name);
        lane.append(element("h2", "", `${laneNames[name]} · ${items.length}`));
        if (items.length) {
            const list = element("ul");
            list.append(...items.map(card));
            lane.append(list);
        }
        else {
            lane.append(element("p", "muted", "No tasks"));
        }
        grid.append(lane);
    }
    if (visible.some((task) => taskStatus(task, tasks) !== "later"))
        result.append(grid);
    const later = visible.filter((task) => taskStatus(task, tasks) === "later");
    if (later.length)
        result.append(backlog(later, "After the pilot"));
    return result;
}
function workdaySpan(first, last) {
    return first === last ? `${first}` : `${first}–${last}`;
}
function gantt(visible) {
    const result = document.createDocumentFragment();
    const dated = visible.filter((task) => scheduled.has(task.id));
    if (dated.length) {
        const days = Math.max(...dated.map((task) => scheduleFor(task.id).end));
        const wrap = element("div", "timeline-scroll");
        wrap.tabIndex = 0;
        wrap.setAttribute("role", "region");
        wrap.setAttribute("aria-label", "Scrollable delivery schedule");
        wrap.style.setProperty("--days", String(days));
        const table = element("table", "timeline");
        const header = table.createTHead().insertRow();
        const taskHeader = element("th", "", "Task and estimated workdays");
        taskHeader.scope = "col";
        const timeHeader = element("th", "timespan");
        timeHeader.scope = "col";
        const weeks = element("div", "track weeks");
        for (let start = 1; start <= days; start += 5) {
            const end = Math.min(start + 4, days);
            const week = element("span", "", `Week ${Math.ceil(start / 5)}`);
            week.style.gridColumn = `${start} / ${end + 1}`;
            weeks.append(week);
        }
        timeHeader.append(weeks);
        header.append(taskHeader, timeHeader);
        const rows = table.createTBody();
        for (const task of dated) {
            const range = scheduleFor(task.id);
            const state = taskStatus(task, tasks);
            const row = rows.insertRow();
            const label = element("th");
            label.scope = "row";
            label.append(taskButton(task), element("small", "muted row-meta", `${task.id} · ${phaseNames[task.phase]} · ${laneNames[state]} · ${timing(task)}`));
            row.append(label);
            const track = element("div", "track");
            const bar = taskButton(task, "bar", workdaySpan(range.start, range.end));
            bar.setAttribute("aria-label", `${task.title}, ${timing(task)}, ${laneNames[state]}`);
            bar.dataset.status = state;
            bar.style.setProperty("--start", String(range.start));
            bar.style.setProperty("--end", String(range.end + 1));
            track.append(bar);
            row.insertCell().append(track);
        }
        wrap.append(table);
        result.append(wrap);
    }
    const unscheduled = visible.filter((task) => !scheduled.has(task.id));
    if (unscheduled.length)
        result.append(backlog(unscheduled, "Unscheduled work"));
    return result;
}
const phase = requiredElement("#phase", HTMLSelectElement);
const search = requiredElement("#search", HTMLInputElement);
const view = requiredElement("#view", HTMLDivElement);
const searchTextByTask = new Map(tasks.map((task) => [task.id, searchableTask(task)]));
function render() {
    const query = search.value.trim().toLowerCase();
    const visible = tasks.filter((task) => (!phase.value || task.phase === phase.value) && searchTextByTask.get(task.id)?.includes(query));
    requiredElement("#result-count", HTMLParagraphElement).textContent =
        `${visible.length} of ${tasks.length} tasks`;
    if (!visible.length) {
        view.replaceChildren(element("p", "empty", "No tasks match these filters."));
    }
    else {
        view.replaceChildren(viewKind === "gantt" ? gantt(visible) : board(visible));
    }
}
requiredElement(".filters", HTMLFormElement).addEventListener("submit", (event) => event.preventDefault());
phase.addEventListener("change", render);
search.addEventListener("input", render);
applyTheme(initialTheme());
themeChoice.addEventListener("change", () => {
    const theme = parseTheme(themeChoice.value) ?? "system";
    applyTheme(theme);
    history.replaceState(history.state, "", themeUrl(location.href, theme));
});
renderSummary();
render();
const selected = byId.get(new URLSearchParams(location.search).get("task") ?? "");
if (selected)
    openTask(selected);
