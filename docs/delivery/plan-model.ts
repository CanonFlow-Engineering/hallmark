type DisplayStatus = Exclude<TaskState, "queued"> | "ready" | "waiting";
type ScheduleRange = { readonly start: number; readonly end: number };
type ScheduledTask = { readonly id: TaskId; readonly range: ScheduleRange };
type ScheduleResult =
  | { readonly kind: "scheduled"; readonly entries: readonly ScheduledTask[] }
  | { readonly kind: "invalid"; readonly taskId: TaskId; readonly dependencyId: TaskId };

function schedulePlan(taskList: readonly DeliveryTask[]): ScheduleResult {
  return taskList.reduce<ScheduleResult>((schedule, task) => {
    if (schedule.kind === "invalid" || task.days === null) return schedule;
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

function taskStatus(task: DeliveryTask, taskList: readonly DeliveryTask[]): DisplayStatus {
  if (task.status !== "queued") return task.status;
  const ready = task.after.every((id) =>
    taskList.some((predecessor) => predecessor.id === id && predecessor.status === "complete"));
  return ready ? "ready" : "waiting";
}

function searchableTask(task: DeliveryTask): string {
  const requirementIds = Array.from(task.refs.matchAll(/CF-(\d{3})(?:–CF-(\d{3}))?/g))
    .flatMap(([, first, last]) => {
      const start = Number(first);
      const end = Number(last ?? first);
      return Array.from({ length: end - start + 1 }, (_, index) =>
        `CF-${String(start + index).padStart(3, "0")}`);
    });
  return `${task.id} ${task.title} ${requirementIds.join(" ")} ${task.detail}`.toLowerCase();
}
