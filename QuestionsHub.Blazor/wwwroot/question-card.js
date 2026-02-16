// Question card interactions for static SSR pages

// Toggle answer visibility
window.toggleAnswer = function(button) {
    const card = button.closest('.question-card');
    if (!card) return;

    const answerSection = card.querySelector('.answer-section');
    if (!answerSection) return;

    const isHidden = answerSection.classList.contains('answer-hidden');

    if (isHidden) {
        answerSection.classList.remove('answer-hidden');
        button.textContent = 'Сховати відповідь';
    } else {
        answerSection.classList.add('answer-hidden');
        button.textContent = 'Показати відповідь';
    }
};

// Toggle all answers visibility on the page
window.toggleAllAnswers = function(button) {
    var answers = document.querySelectorAll('.answer-section');
    var toggles = document.querySelectorAll('.answer-toggle');
    var anyHidden = false;

    answers.forEach(function(el) {
        if (el.classList.contains('answer-hidden')) anyHidden = true;
    });

    if (anyHidden) {
        // Show all
        answers.forEach(function(el) { el.classList.remove('answer-hidden'); });
        toggles.forEach(function(el) { el.textContent = 'Сховати відповідь'; });
        button.querySelector('.toggle-all-label').textContent = 'Сховати всі відповіді';
        // Swap icon to eye-slash
        var use = button.querySelector('.toggle-all-icon use');
        if (use) {
            var spriteBase = (window.__iconsSvgUrl || '/icons.svg');
            use.setAttribute('href', spriteBase + '#i-eye-slash');
        }
    } else {
        // Hide all
        answers.forEach(function(el) { el.classList.add('answer-hidden'); });
        toggles.forEach(function(el) { el.textContent = 'Показати відповідь'; });
        button.querySelector('.toggle-all-label').textContent = 'Показати всі відповіді';
        // Swap icon to eye
        var use = button.querySelector('.toggle-all-icon use');
        if (use) {
            var spriteBase = (window.__iconsSvgUrl || '/icons.svg');
            use.setAttribute('href', spriteBase + '#i-eye');
        }
    }
};

// Copy question link to clipboard
window.copyQuestionLink = function(button, questionId) {
    const url = window.location.origin + '/question/' + questionId;

    // Use existing copyToClipboard if available, otherwise inline
    const copyPromise = window.copyToClipboard
        ? window.copyToClipboard(url)
        : navigator.clipboard.writeText(url);

    copyPromise.then(function(success) {
        if (success === false) return;

        const icon = button.querySelector('.copy-icon');
        if (icon) {
            const useElement = icon.querySelector('use');
            if (useElement) {
                const originalHref = useElement.getAttribute('href');
                // Extract base sprite URL (with version query) from the original href
                var spriteBase = (window.__iconsSvgUrl || '/icons.svg');
                // Show check icon
                useElement.setAttribute('href', spriteBase + '#i-check');
                icon.classList.add('copied');

                setTimeout(function() {
                    // Restore original icon
                    useElement.setAttribute('href', originalHref);
                    icon.classList.remove('copied');
                }, 2000);
            }
        }
    }).catch(function(err) {
        console.error('Copy failed:', err);
    });
};
