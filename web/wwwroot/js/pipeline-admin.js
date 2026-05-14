(() => {
    const form = document.getElementById('pipeline-editor-form');
    if (!form) return;

    const list = form.querySelector('[data-stage-list]');
    const template = document.getElementById('stage-template');
    const addButton = form.querySelector('[data-add-stage]');

    function reindex() {
        [...list.querySelectorAll('[data-stage-card]')].forEach((card, index) => {
            card.querySelectorAll('[name]').forEach(input => {
                input.name = input.name.replace(/Stages\[(?:\d+|__index__)\]/, `Stages[${index}]`);
            });
            card.querySelectorAll('[id]').forEach(input => {
                input.id = input.id.replace(/Stages\[(?:\d+|__index__)\]/, `Stages[${index}]`);
            });
            const eyebrow = card.querySelector('.eyebrow');
            if (eyebrow) eyebrow.textContent = `Stage ${index + 1}`;
        });
    }

    addButton?.addEventListener('click', () => {
        const wrapper = document.createElement('div');
        wrapper.innerHTML = template.innerHTML.trim();
        const card = wrapper.firstElementChild;
        list.appendChild(card);
        reindex();
        card.querySelector('input')?.focus();
    });

    list.addEventListener('click', event => {
        const button = event.target.closest('[data-remove-stage]');
        if (!button) return;
        button.closest('[data-stage-card]')?.remove();
        reindex();
    });
})();
