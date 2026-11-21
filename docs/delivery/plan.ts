const tasks = deliveryPlan.tasks;
const byId = new Map<string, DeliveryTask>(tasks.map((task) => [task.id, task]));
const viewKind = document.body.dataset.view;
if (viewKind !== "gantt" && viewKind !== "kanban") throw new Error("Unknown delivery view");

const phaseNames: Record<PlanPhase, string> = {
  pilot: "Wallet pilot", mvp: "Public MVP", later: "After the pilot"
};
const laneNames: Record<DisplayStatus, string> = {
  ready: "Ready", active: "In progress", blocked: "Blocked",
  waiting: "Waiting", complete: "Complete", later: "Later"
};
const planSchedule = schedulePlan(tasks);
if (planSchedule.kind === "invalid") {
  throw new Error(`Task ${planSchedule.taskId} needs an earlier scheduled dependency ${planSchedule.dependencyId}`);
}
const scheduled = new Map(planSchedule.entries.map((entry) => [entry.id, entry.range]));

function taskById(id: TaskId): DeliveryTask {
  const task = byId.get(id);
  if (!task) throw new Error(`Unknown dependency ${id}`);
  return task;
}

function scheduleFor(id: TaskId): ScheduleRange {
  const range = scheduled.get(id);
  if (!range) throw new Error(`Scheduled work requires an earlier scheduled dependency ${id}`);
  return range;
}

function element<Tag extends keyof HTMLElementTagNameMap>(
  tag: Tag, className = "", text = ""
): HTMLElementTagNameMap[Tag] {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text) node.textContent = text;
  return node;
}

function requiredElement<T extends HTMLElement>(selector: string, kind: new () => T): T {
  const node = document.querySelector(selector);
  if (!(node instanceof kind)) throw new Error(`Missing or invalid element ${selector}`);
  return node;
}

type ThemePreference = "system" | "light" | "dark";
const themeStorageKey = "hallmark-delivery-theme";
const themeChoice = requiredElement("#theme", HTMLSelectElement);

function parseTheme(value: string | null): ThemePreference | undefined {
  return value === "system" || value === "light" || value === "dark" ? value : undefined;
}

function initialTheme(): ThemePreference {
  const linked = parseTheme(new URLSearchParams(location.search).get("theme"));
  if (linked) return linked;
  try {
    return parseTheme(localStorage.getItem(themeStorageKey)) ?? "system";
  } catch {
    return "system";
  }
}

function themeUrl(href: string, theme: ThemePreference): string {
  const url = new URL(href, location.href);
  url.searchParams.set("theme", theme);
  return url.href;
}

function applyTheme(theme: ThemePreference): void {
  document.documentElement.dataset.theme = theme;
  themeChoice.value = theme;
  document.querySelectorAll<HTMLAnchorElement>("a[data-view-link]").forEach((link) => {
    link.href = themeUrl(link.href, theme);
  });
  try {
    localStorage.setItem(themeStorageKey, theme);
  } catch {
    // Links carry the preference when storage is unavailable for local files.
  }
}

function timing(task: DeliveryTask): string {
  const range = scheduled.get(task.id);
  if (!range) return "Unscheduled";
  return range.start === range.end ? `Day ${range.start}` : `Days ${range.start}–${range.end}`;
}

function taskButton(task: DeliveryTask, className = "task-link", text = task.title): HTMLButtonElement {
  const button = element("button", className, text);
  button.type = "button";
  button.dataset.task = task.id;
  button.addEventListener("click", () => openTask(task));
  return button;
}

const dialog = requiredElement("#task-dialog", HTMLDialogElement);
const detail = requiredElement("#task-detail", HTMLDivElement);

function addDetail(list: HTMLDListElement, label: string, value: string): void {
  list.append(element("dt", "", label), element("dd", "", value));
}

function openTask(task: DeliveryTask): void {
  requiredElement("#task-title", HTMLHeadingElement).textContent = task.title;
  requiredElement("#task-meta", HTMLParagraphElement).textContent =
    `${task.id} · ${phaseNames[task.phase]} · ${laneNames[taskStatus(task, tasks)]} · ${timing(task)}`;
  const fields = element("dl");
  if (task.after.length) {
    addDetail(fields, "Depends on", task.after.map((id) => taskById(id).title).join(", "));
  }
  addDetail(fields, "Acceptance check", task.acceptance);
  addDetail(fields, "Requirements", task.refs);
  if (task.evidence) addDetail(fields, "Observed evidence", task.evidence);
  const otherView = viewKind === "gantt" ? "Kanban" : "Gantt";
  const alternative = element("a", "alternate", `Open in ${otherView}`);
  const taskPath = `${otherView.toLowerCase()}.html?task=${encodeURIComponent(task.id)}`;
  alternative.href = themeUrl(taskPath, parseTheme(themeChoice.value) ?? "system");
  detail.replaceChildren(element("p", "", task.detail), fields, alternative);
  dialog.showModal();
}

function renderSummary(): void {
  const pilot = tasks.filter((task) => task.phase === "pilot");
  const complete = pilot.filter((task) => task.status === "complete").length;
  const lastPilot = pilot.at(-1);
  const pilotFinish = lastPilot && scheduled.get(lastPilot.id)?.end;
  const indicators: readonly (readonly [string, string])[] = [
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
  } else {
    panel.append(element("p", "", "No task is ready."));
  }
}

function card(task: DeliveryTask): HTMLLIElement {
  const item = element("li", "card");
  item.dataset.task = task.id;
  item.append(
    element("span", "card-meta", `${task.id} · ${phaseNames[task.phase]} · ${timing(task)}`),
    taskButton(task),
    element("p", "", task.detail)
  );
  return item;
}

function backlog(items: readonly DeliveryTask[], title: string): HTMLElement {
  const section = element("section", "backlog");
  section.append(element("h2", "", title));
  const list = element("ul");
  list.append(...items.map(card));
  section.append(list);
  return section;
}

function board(visible: readonly DeliveryTask[]): DocumentFragment {
  const result = document.createDocumentFragment();
  const grid = element("div", "board");
  const lanes: DisplayStatus[] = ["ready", "active", "waiting", "complete"];
  if (visible.some((task) => taskStatus(task, tasks) === "blocked")) lanes.splice(2, 0, "blocked");
  for (const name of lanes) {
    const lane = element("section", "lane");
    lane.dataset.status = name;
    const items = visible.filter((task) => taskStatus(task, tasks) === name);
    lane.append(element("h2", "", `${laneNames[name]} · ${items.length}`));
    if (items.length) {
      const list = element("ul");
      list.append(...items.map(card));
      lane.append(list);
    } else {
      lane.append(element("p", "muted", "No tasks"));
    }
    grid.append(lane);
  }
  if (visible.some((task) => taskStatus(task, tasks) !== "later")) result.append(grid);
  const later = visible.filter((task) => taskStatus(task, tasks) === "later");
  if (later.length) result.append(backlog(later, "After the pilot"));
  return result;
}

function workdaySpan(first: number, last: number): string {
  return first === last ? `${first}` : `${first}–${last}`;
}

function gantt(visible: readonly DeliveryTask[]): DocumentFragment {
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
      label.append(taskButton(task), element("small", "muted row-meta",
        `${task.id} · ${phaseNames[task.phase]} · ${laneNames[state]} · ${timing(task)}`));
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
  if (unscheduled.length) result.append(backlog(unscheduled, "Unscheduled work"));
  return result;
}

const phase = requiredElement("#phase", HTMLSelectElement);
const search = requiredElement("#search", HTMLInputElement);
const view = requiredElement("#view", HTMLDivElement);
const searchTextByTask = new Map<TaskId, string>(tasks.map((task) => [task.id, searchableTask(task)]));

function render(): void {
  const query = search.value.trim().toLowerCase();
  const visible = tasks.filter((task) =>
    (!phase.value || task.phase === phase.value) && searchTextByTask.get(task.id)?.includes(query));
  requiredElement("#result-count", HTMLParagraphElement).textContent =
    `${visible.length} of ${tasks.length} tasks`;
  if (!visible.length) {
    view.replaceChildren(element("p", "empty", "No tasks match these filters."));
  } else {
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
if (selected) openTask(selected);
