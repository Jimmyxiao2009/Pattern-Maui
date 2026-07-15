const root = document.documentElement;
const navItems = [...document.querySelectorAll('.nav-item')];
const views = [...document.querySelectorAll('.view')];
const title = document.querySelector('#view-title');
const toast = document.querySelector('#toast');
let toastTimer;

function showToast(message) {
  toast.textContent = message;
  toast.classList.add('show');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => toast.classList.remove('show'), 2200);
}

navItems.forEach((item) => item.addEventListener('click', () => {
  navItems.forEach((nav) => nav.classList.toggle('active', nav === item));
  views.forEach((view) => view.classList.toggle('active', view.id === `${item.dataset.view}-view`));
  title.textContent = item.dataset.title;
}));

document.querySelector('#message-form').addEventListener('submit', (event) => {
  event.preventDefault();
  const input = document.querySelector('#message-input');
  const value = input.value.trim();
  if (!value) return;
  const message = document.createElement('article');
  message.className = 'message user';
  message.innerHTML = `<p></p><time>${new Date().toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' })}</time>`;
  message.querySelector('p').textContent = value;
  document.querySelector('#chat-stream').append(message);
  input.value = '';
  message.scrollIntoView({ behavior: 'smooth', block: 'end' });
  showToast('消息已交给陪伴槽');
});

document.querySelector('#message-input').addEventListener('keydown', (event) => {
  if (event.key === 'Enter' && !event.shiftKey) {
    event.preventDefault();
    document.querySelector('#message-form').requestSubmit();
  }
});

document.querySelector('#new-chat').addEventListener('click', () => showToast('已创建新对话'));

const search = document.querySelector('#memory-search');
let activeFilter = 'all';
function filterMemories() {
  const query = search.value.trim().toLowerCase();
  let visible = 0;
  document.querySelectorAll('.memory-card').forEach((card) => {
    const matchesFilter = activeFilter === 'all' || card.dataset.category === activeFilter;
    const matchesQuery = card.textContent.toLowerCase().includes(query);
    card.classList.toggle('hidden', !(matchesFilter && matchesQuery));
    if (matchesFilter && matchesQuery) visible += 1;
  });
  document.querySelector('#memory-empty').style.display = visible ? 'none' : 'block';
}
search.addEventListener('input', filterMemories);
document.querySelectorAll('#memory-filters button').forEach((button) => button.addEventListener('click', () => {
  activeFilter = button.dataset.filter;
  document.querySelectorAll('#memory-filters button').forEach((item) => item.classList.toggle('active', item === button));
  filterMemories();
}));

document.querySelectorAll('#theme-toggle button').forEach((button) => button.addEventListener('click', () => {
  root.dataset.theme = button.dataset.theme;
  document.querySelectorAll('#theme-toggle button').forEach((item) => item.classList.toggle('active', item === button));
  localStorage.setItem('pattern-theme', button.dataset.theme);
}));

const savedTheme = localStorage.getItem('pattern-theme');
if (savedTheme) {
  root.dataset.theme = savedTheme;
  document.querySelectorAll('#theme-toggle button').forEach((button) => button.classList.toggle('active', button.dataset.theme === savedTheme));
}

document.querySelectorAll('.switch input').forEach((input) => input.addEventListener('change', () => showToast(input.checked ? '已开启' : '已关闭')));
