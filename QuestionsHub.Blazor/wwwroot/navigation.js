// Handle smooth scrolling for anchor links in Blazor interactive mode
window.scrollToElement = function(elementId) {
    console.log('scrollToElement called with:', elementId);
    const element = document.getElementById(elementId);
    if (element) {
        console.log('Element found:', element);
        
        // Get element position and add offset for fixed header/better spacing
        const elementPosition = element.getBoundingClientRect().top + window.pageYOffset;
        const offsetPosition = elementPosition - 100; // 100px offset for navbar and better spacing
        
        window.scrollTo({
            top: offsetPosition,
            behavior: 'smooth'
        });
        return true;
    } else {
        console.error('Element not found with id:', elementId);
    }
    return false;
};

// Copy text to clipboard
window.copyToClipboard = async function(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch (err) {
        console.error('Failed to copy to clipboard:', err);
        return false;
    }
};

