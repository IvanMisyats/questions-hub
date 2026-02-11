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
