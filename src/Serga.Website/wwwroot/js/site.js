// Native details menus remain usable without JavaScript.
const shell = document.querySelector('.navigation-shell');
const groups = [...document.querySelectorAll('.nav-group')];
const desktop = window.matchMedia('(min-width: 961px)');
function syncNavigation() { shell.open = desktop.matches; }
syncNavigation();
desktop.addEventListener('change', syncNavigation);
groups.forEach(group => group.addEventListener('toggle', () => {
    if (group.open) groups.filter(other => other !== group).forEach(other => { other.open = false; });
}));
document.addEventListener('click', event => {
    if (!event.target.closest('.nav-group')) groups.forEach(group => { group.open = false; });
});
document.addEventListener('keydown', event => {
    if (event.key !== 'Escape') return;
    const active = groups.find(group => group.open);
    if (active) { active.open = false; active.querySelector('summary').focus(); }
    else if (!desktop.matches && shell.open) { shell.open = false; shell.querySelector('summary').focus(); }
});
