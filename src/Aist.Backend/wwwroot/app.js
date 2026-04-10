const apiBase = window.location.protocol === "file:"
  ? "http://localhost:5192/api/v1"
  : "/api/v1";
const refreshIntervalMs = 3000;

const elements = {
  projectSelect: document.getElementById("projectSelect"),
  taskSearch: document.getElementById("taskSearch"),
  statusFilter: document.getElementById("statusFilter"),
  jobsList: document.getElementById("jobsList"),
  jobCount: document.getElementById("jobCount"),
  details: document.getElementById("details"),
  taskTemplate: document.getElementById("taskTemplate")
};

const state = {
  projects: [],
  jobs: [],
  selectedProjectId: null,
  selectedJobId: null,
  query: "",
  status: "all",
  refreshTimer: null,
  isRefreshing: false,
  isLoadingProject: false
};

const statusNames = ["Todo", "InProgress", "Done"];
const typeNames = ["Feature", "Fix", "Refactor", "Chore", "Fmt", "Doc"];

function toName(value, names) {
  if (typeof value === "number") {
    return names[value] || String(value);
  }
  return value || "Unknown";
}

function formatDate(iso) {
  if (!iso) {
    return "-";
  }

  const dt = new Date(iso);
  return new Intl.DateTimeFormat("ru-RU", {
    dateStyle: "medium",
    timeStyle: "short"
  }).format(dt);
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

async function fetchJson(path) {
  const response = await fetch(`${apiBase}${path}`);
  if (!response.ok) {
    throw new Error(`HTTP ${response.status}`);
  }
  return response.json();
}

function renderProjects() {
  elements.projectSelect.innerHTML = state.projects
    .map((project) => `<option value="${project.id}">${escapeHtml(project.title)}</option>`)
    .join("");

  if (!state.selectedProjectId && state.projects.length > 0) {
    state.selectedProjectId = state.projects[0].id;
    elements.projectSelect.value = state.selectedProjectId;
  }
}

function getFilteredJobs() {
  return state.jobs.filter((job) => {
    const statusName = toName(job.status, statusNames);
    const text = [job.title, job.shortSlug, job.description]
      .join(" ")
      .toLowerCase();

    const statusMatch = state.status === "all" || statusName === state.status;
    const queryMatch = state.query.length === 0 || text.includes(state.query);
    return statusMatch && queryMatch;
  });
}

function renderJobs() {
  const filtered = getFilteredJobs();
  elements.jobCount.textContent = String(filtered.length);

  if (filtered.length === 0) {
    elements.jobsList.innerHTML = '<div class="empty-state">По выбранным фильтрам задач нет.</div>';
    if (state.selectedJobId) {
      state.selectedJobId = null;
      renderDetails();
    }
    return;
  }

  if (!state.selectedJobId || !filtered.some((job) => job.id === state.selectedJobId)) {
    state.selectedJobId = filtered[0].id;
  }

  elements.jobsList.innerHTML = filtered
    .map((job) => {
      const statusName = toName(job.status, statusNames);
      const typeName = toName(job.type, typeNames);
      const activeClass = state.selectedJobId === job.id ? "active" : "";
      const desc = job.description?.trim().length > 0 ? job.description : "Описание отсутствует";

      return `
        <button class="job-item ${activeClass}" data-job-id="${job.id}" type="button">
          <p class="job-title">${escapeHtml(job.title)}</p>
          <p class="job-desc">${escapeHtml(desc)}</p>
          <div class="job-meta">
            <span class="pill">${escapeHtml(job.shortSlug)}</span>
            <span class="pill">${escapeHtml(typeName)}</span>
            <span class="pill status-${escapeHtml(statusName)}">${escapeHtml(statusName)}</span>
          </div>
        </button>
      `;
    })
    .join("");
}

function buildStories(stories) {
  if (!stories || stories.length === 0) {
    return '<div class="empty-state">Для этой задачи пока нет user stories.</div>';
  }

  return stories
    .slice()
    .sort((a, b) => a.priority - b.priority)
    .map((story) => {
      const criteria = (story.acceptanceCriterias || [])
        .map((item) => {
          const criteriaClass = item.isMet ? "met" : "unmet";
          const marker = item.isMet ? "[x]" : "[ ]";
          return `<li class="${criteriaClass}">${marker} ${escapeHtml(item.description)}</li>`;
        })
        .join("");

      const logsSorted = (story.progressLogs || [])
        .slice()
        .sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt));

      const logs = logsSorted
        .map((log) => `
          <li>
            <p class="log-text">${escapeHtml(log.text)}</p>
            <span class="log-time">${formatDate(log.createdAt)}</span>
          </li>
        `)
        .join("");

      const logCountLabel = `${logsSorted.length} запис${logsSorted.length === 1 ? "ь" : logsSorted.length < 5 ? "и" : "ей"}`;
      const logsBlock = logs
        ? `
          <details class="logs" ${logsSorted.length <= 2 ? "open" : ""}>
            <summary>
              <span>Progress Logs</span>
              <span class="pill">${logCountLabel}</span>
            </summary>
            <ul>${logs}</ul>
          </details>
        `
        : '<div class="logs"><p class="story-meta">Логи отсутствуют.</p></div>';

      const statusLabel = story.isComplete ? "Complete" : "In progress";

      return `
        <article class="story">
          <div class="story-top">
            <div class="story-top-main">
              <h4 class="story-title">${escapeHtml(story.title)}</h4>
              <div class="story-meta">${statusLabel}</div>
            </div>
            <span class="pill">Priority ${story.priority}</span>
          </div>

          <div class="story-grid">
            <div class="story-cell"><span>Who</span>${escapeHtml(story.who)}</div>
            <div class="story-cell"><span>What</span>${escapeHtml(story.what)}</div>
            <div class="story-cell"><span>Why</span>${escapeHtml(story.why)}</div>
          </div>

          <div class="criteria">
            <h4>Acceptance Criteria</h4>
            ${criteria ? `<ul>${criteria}</ul>` : '<p class="story-meta">Нет критериев.</p>'}
          </div>

          ${logsBlock}
        </article>
      `;
    })
    .join("");
}

function renderDetails(jobData) {
  if (!jobData) {
    elements.details.innerHTML = '<div class="empty-state">Выберите задачу слева, чтобы увидеть историю и логи.</div>';
    return;
  }

  const fragment = elements.taskTemplate.content.cloneNode(true);
  const statusName = toName(jobData.status, statusNames);
  const typeName = toName(jobData.type, typeNames);

  fragment.querySelector(".task-title").textContent = jobData.title;
  fragment.querySelector(".slug").textContent = jobData.shortSlug;
  fragment.querySelector(".type").textContent = typeName;

  const status = fragment.querySelector(".status");
  status.textContent = statusName;
  status.classList.add(`status-${statusName}`);

  fragment.querySelector(".created").textContent = formatDate(jobData.createdAt);
  fragment.querySelector(".task-description").textContent = jobData.description || "Описание отсутствует.";

  const stories = jobData.userStories || [];
  const completedStories = stories.filter((story) => story.isComplete).length;

  fragment.querySelector(".story-count").textContent = `${stories.length} шт.`;
  fragment.querySelector(".story-progress").textContent = `${completedStories}/${stories.length} завершено`;
  fragment.querySelector(".stories").innerHTML = buildStories(stories);

  elements.details.innerHTML = "";
  elements.details.append(fragment);
}

async function loadJobDetails(jobId, options = {}) {
  const showLoading = options.showLoading ?? true;
  if (showLoading) {
    elements.details.innerHTML = '<div class="empty-state">Загружаю детали задачи...</div>';
  }

  try {
    const job = await fetchJson(`/jobs/${jobId}`);
    const stories = await fetchJson(`/userstories/by-job/${jobId}`);
    if (state.selectedJobId !== jobId) {
      return;
    }

    job.userStories = stories;
    renderDetails(job);
  } catch {
    if (showLoading) {
      elements.details.innerHTML = '<div class="empty-state">Не удалось загрузить историю задачи.</div>';
    }
  }
}

async function loadJobsForProject(projectId) {
  state.isLoadingProject = true;
  elements.jobsList.innerHTML = '<div class="empty-state">Загружаю задачи...</div>';

  try {
    const jobs = await fetchJson(`/jobs?projectId=${projectId}`);
    if (state.selectedProjectId !== projectId) {
      return;
    }

    state.jobs = jobs;
    renderJobs();
    if (state.selectedJobId) {
      await loadJobDetails(state.selectedJobId);
    } else {
      renderDetails();
    }
  } catch {
    elements.jobsList.innerHTML = '<div class="empty-state">Не удалось загрузить задачи.</div>';
    elements.jobCount.textContent = "0";
    renderDetails();
  } finally {
    state.isLoadingProject = false;
  }
}

async function refreshCurrentProject() {
  if (!state.selectedProjectId || state.isRefreshing || state.isLoadingProject || document.hidden) {
    return;
  }

  const projectId = state.selectedProjectId;
  state.isRefreshing = true;

  try {
    const jobs = await fetchJson(`/jobs?projectId=${projectId}`);
    if (state.selectedProjectId !== projectId) {
      return;
    }

    state.jobs = jobs;
    renderJobs();

    if (state.selectedJobId) {
      await loadJobDetails(state.selectedJobId, { showLoading: false });
    } else {
      renderDetails();
    }
  } catch {
    // Keep the last good data visible during transient refresh failures.
  } finally {
    state.isRefreshing = false;
  }
}

function startAutoRefresh() {
  if (state.refreshTimer) {
    window.clearInterval(state.refreshTimer);
  }

  state.refreshTimer = window.setInterval(refreshCurrentProject, refreshIntervalMs);
}

function bindAutoRefreshEvents() {
  document.addEventListener("visibilitychange", () => {
    if (!document.hidden) {
      void refreshCurrentProject();
    }
  });
}

function bindEvents() {
  elements.projectSelect.addEventListener("change", async (event) => {
    state.selectedProjectId = event.target.value;
    state.selectedJobId = null;
    await loadJobsForProject(state.selectedProjectId);
  });

  elements.taskSearch.addEventListener("input", (event) => {
    state.query = event.target.value.trim().toLowerCase();
    renderJobs();
  });

  elements.statusFilter.addEventListener("change", (event) => {
    state.status = event.target.value;
    renderJobs();
  });

  elements.jobsList.addEventListener("click", async (event) => {
    const button = event.target.closest(".job-item");
    if (!button) {
      return;
    }

    const nextId = button.getAttribute("data-job-id");
    if (!nextId || state.selectedJobId === nextId) {
      return;
    }

    state.selectedJobId = nextId;
    renderJobs();
    await loadJobDetails(nextId);
  });
}

async function start() {
  bindEvents();
  elements.jobsList.innerHTML = '<div class="empty-state">Загружаю проекты...</div>';

  try {
    state.projects = await fetchJson("/projects");
  } catch {
    elements.jobsList.innerHTML = '<div class="empty-state">Не удалось загрузить проекты.</div>';
    elements.jobCount.textContent = "0";
    return;
  }

  if (state.projects.length === 0) {
    renderProjects();
    elements.jobsList.innerHTML = '<div class="empty-state">Проекты отсутствуют. Создайте проект через CLI/API.</div>';
    elements.jobCount.textContent = "0";
    return;
  }

  renderProjects();
  await loadJobsForProject(state.selectedProjectId);
  startAutoRefresh();
  bindAutoRefreshEvents();
}

start();
