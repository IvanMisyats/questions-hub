// Sortable.js interop for Blazor
window.sortableInterop = {
    instances: {},

    // Helper: Collect full question order for a container (tour or block)
    collectQuestionOrder: function (container) {
        const items = container.querySelectorAll('.list-group-item[data-question-id]');
        const result = [];
        items.forEach(item => {
            result.push({
                questionId: parseInt(item.dataset.questionId),
                blockId: container.dataset.blockId ? parseInt(container.dataset.blockId) : null
            });
        });
        return result;
    },

    // Helper: Collect all question orders for a tour (across all blocks if any)
    collectTourQuestionOrder: function (tourId) {
        // Find all question containers for this tour
        const containers = document.querySelectorAll(`[data-tour-id="${tourId}"]`);
        const result = [];

        containers.forEach(container => {
            const items = container.querySelectorAll('.list-group-item[data-question-id]');
            const blockId = container.dataset.blockId ? parseInt(container.dataset.blockId) : null;

            items.forEach(item => {
                result.push({
                    questionId: parseInt(item.dataset.questionId),
                    blockId: blockId
                });
            });
        });

        return result;
    },

    // Initialize sortable for tours
    initToursSortable: function (elementId, dotNetRef) {
        const element = document.getElementById(elementId);
        if (!element) {
            console.warn('Tours container not found:', elementId);
            return;
        }

        // Check if Sortable is available
        if (typeof Sortable === 'undefined') {
            console.error('SortableJS library not loaded');
            return;
        }

        // Destroy existing instance if any
        if (this.instances[elementId]) {
            this.instances[elementId].destroy();
        }

        this.instances[elementId] = new Sortable(element, {
            animation: 150,
            handle: '.tour-drag-handle',
            ghostClass: 'sortable-ghost',
            chosenClass: 'sortable-chosen',
            dragClass: 'sortable-drag',
            draggable: '.accordion-item',
            forceFallback: true,  // Use JS-based drag instead of native HTML5
            onEnd: function (evt) {
                const tourIds = Array.from(element.querySelectorAll('[data-tour-id]'))
                    .map(el => parseInt(el.dataset.tourId));
                dotNetRef.invokeMethodAsync('OnToursReordered', tourIds);
            }
        });

        console.log('Tours sortable initialized for:', elementId);
    },

    // Initialize sortable for blocks in a tour
    initBlocksSortable: function (elementId, tourId, dotNetRef) {
        const element = document.getElementById(elementId);
        if (!element) {
            console.warn('Blocks container not found:', elementId);
            return;
        }

        // Check if Sortable is available
        if (typeof Sortable === 'undefined') {
            console.error('SortableJS library not loaded');
            return;
        }

        const instanceKey = `blocks_${elementId}`;

        // Destroy existing instance if any
        if (this.instances[instanceKey]) {
            this.instances[instanceKey].destroy();
        }

        this.instances[instanceKey] = new Sortable(element, {
            animation: 150,
            handle: '.block-drag-handle',
            ghostClass: 'sortable-ghost',
            chosenClass: 'sortable-chosen',
            dragClass: 'sortable-drag',
            draggable: '.block-item',
            forceFallback: true,
            onEnd: function (evt) {
                const blockIds = Array.from(element.querySelectorAll('[data-block-id]'))
                    .map(el => parseInt(el.dataset.blockId));
                dotNetRef.invokeMethodAsync('OnBlocksReordered', tourId, blockIds);
            }
        });

        console.log('Blocks sortable initialized for:', elementId);
    },

    // Initialize sortable for questions in a tour (without blocks)
    initQuestionsSortable: function (elementId, tourId, dotNetRef) {
        const element = document.getElementById(elementId);
        if (!element) {
            console.warn('Questions container not found:', elementId);
            return;
        }

        // Check if Sortable is available
        if (typeof Sortable === 'undefined') {
            console.error('SortableJS library not loaded');
            return;
        }

        const instanceKey = `questions_${elementId}`;

        // Destroy existing instance if any
        if (this.instances[instanceKey]) {
            this.instances[instanceKey].destroy();
        }

        this.instances[instanceKey] = new Sortable(element, {
            group: 'questions', // Allow dragging between tours/blocks
            animation: 150,
            handle: '.question-drag-handle',
            ghostClass: 'sortable-ghost',
            chosenClass: 'sortable-chosen',
            dragClass: 'sortable-drag',
            draggable: '.list-group-item[data-question-id]',
            filter: '.text-muted', // Exclude placeholder items
            emptyInsertThreshold: 20,
            forceFallback: true,
            onAdd: function (evt) {
                // Remove placeholder if it exists when an item is added
                const placeholder = element.querySelector('.list-group-item.text-muted');
                if (placeholder) {
                    placeholder.style.display = 'none';
                }
            },
            onEnd: function (evt) {
                // Only process if dragging an actual question (has data-question-id)
                if (!evt.item.dataset.questionId) return;

                // Get source and target tour IDs
                const fromTourId = parseInt(evt.from.dataset.tourId);
                const toTourId = parseInt(evt.to.dataset.tourId);

                // Collect full question order for affected tour(s)
                const toTourOrder = window.sortableInterop.collectTourQuestionOrder(toTourId);

                // If moving between tours, also collect source tour order
                let fromTourOrder = null;
                if (fromTourId !== toTourId) {
                    fromTourOrder = window.sortableInterop.collectTourQuestionOrder(fromTourId);
                }

                // Revert the DOM change - move the item back to its original position
                // Blazor will re-render the correct state
                if (evt.from !== evt.to) {
                    if (evt.oldIndex < evt.from.children.length) {
                        evt.from.insertBefore(evt.item, evt.from.children[evt.oldIndex]);
                    } else {
                        evt.from.appendChild(evt.item);
                    }
                } else if (evt.oldIndex !== evt.newIndex) {
                    if (evt.oldIndex < evt.from.children.length) {
                        evt.from.insertBefore(evt.item, evt.from.children[evt.oldIndex]);
                    } else {
                        evt.from.appendChild(evt.item);
                    }
                }

                // Send full order to .NET
                dotNetRef.invokeMethodAsync('OnTourQuestionsReordered', toTourId, toTourOrder, fromTourId, fromTourOrder);
            }
        });

        console.log('Questions sortable initialized for:', elementId);
    },

    // Initialize sortable for questions in a block
    initBlockQuestionsSortable: function (elementId, tourId, blockId, dotNetRef) {
        const element = document.getElementById(elementId);
        if (!element) {
            console.warn('Block questions container not found:', elementId);
            return;
        }

        // Check if Sortable is available
        if (typeof Sortable === 'undefined') {
            console.error('SortableJS library not loaded');
            return;
        }

        const instanceKey = `blockquestions_${elementId}`;

        // Destroy existing instance if any
        if (this.instances[instanceKey]) {
            this.instances[instanceKey].destroy();
        }

        this.instances[instanceKey] = new Sortable(element, {
            group: 'questions', // Same group allows dragging between blocks and tours
            animation: 150,
            handle: '.question-drag-handle',
            ghostClass: 'sortable-ghost',
            chosenClass: 'sortable-chosen',
            dragClass: 'sortable-drag',
            draggable: '.list-group-item[data-question-id]',
            filter: '.text-muted', // Exclude placeholder items
            emptyInsertThreshold: 20, // Larger threshold for dropping into empty containers
            forceFallback: true,
            onAdd: function (evt) {
                // Remove placeholder if it exists when an item is added
                const placeholder = element.querySelector('.list-group-item.text-muted');
                if (placeholder) {
                    placeholder.style.display = 'none';
                }
            },
            onEnd: function (evt) {
                // Only process if dragging an actual question (has data-question-id)
                if (!evt.item.dataset.questionId) return;

                // Get source and target tour IDs
                const fromTourId = parseInt(evt.from.dataset.tourId);
                const toTourId = parseInt(evt.to.dataset.tourId);

                // Collect full question order for affected tour(s)
                const toTourOrder = window.sortableInterop.collectTourQuestionOrder(toTourId);

                // If moving between tours, also collect source tour order
                let fromTourOrder = null;
                if (fromTourId !== toTourId) {
                    fromTourOrder = window.sortableInterop.collectTourQuestionOrder(fromTourId);
                }

                // Revert the DOM change - move the item back to its original position
                // Blazor will re-render the correct state
                if (evt.from !== evt.to) {
                    if (evt.oldIndex < evt.from.children.length) {
                        evt.from.insertBefore(evt.item, evt.from.children[evt.oldIndex]);
                    } else {
                        evt.from.appendChild(evt.item);
                    }
                } else if (evt.oldIndex !== evt.newIndex) {
                    if (evt.oldIndex < evt.from.children.length) {
                        evt.from.insertBefore(evt.item, evt.from.children[evt.oldIndex]);
                    } else {
                        evt.from.appendChild(evt.item);
                    }
                }

                // Send full order to .NET
                dotNetRef.invokeMethodAsync('OnTourQuestionsReordered', toTourId, toTourOrder, fromTourId, fromTourOrder);
            }
        });

        console.log('Block questions sortable initialized for:', elementId);
    },

    // Destroy all sortable instances
    destroyAll: function () {
        for (const key in this.instances) {
            if (this.instances[key]) {
                this.instances[key].destroy();
            }
        }
        this.instances = {};
    },

    // Destroy specific instance
    destroy: function (elementId) {
        if (this.instances[elementId]) {
            this.instances[elementId].destroy();
            delete this.instances[elementId];
        }
    }
};
