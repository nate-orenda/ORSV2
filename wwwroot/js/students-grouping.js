// Students Grouping and Table Management
window.StudentGrouping = (function() {
    let currentGrouping = [];
    let dataTable = null;
    const columnMap = new Map();
    // Remember user's last non-group order so sorting stays within groups
    let lastUserOrder = [[0, 'asc']];
    let groupSort = new Map();   // columnIndex -> 'asc' | 'desc'
    let suppressOrderBounce = false; // prevents infinite order loops

    function init() {
        buildColumnMap();
        initializeModal();
        initializeDragAndDrop();
        initializeDataTable();
        setupGroupSelectionEvents();
        loadJQueryUI();
    }

    function buildColumnMap() {
        $('#studentsTable thead th').each(function() {
            const columnName = $(this).data('column');
            if (columnName) {
                const columnIndex = $(this).index();
                if (columnIndex === -1) {
                    console.warn('Could not resolve column index for', columnName);
                } else {
                    columnMap.set(columnName, columnIndex);
                }
            }
        });
    }

    function initializeModal() {
        const modal = document.getElementById('profileModal');
        modal.addEventListener('show.bs.modal', function(event) {
            const button = event.relatedTarget;
            const studentId = button.getAttribute('data-student-id');
            fetch(`/GuidanceAlignment/GAProfileCard?id=${studentId}`)
                .then(response => response.text())
                .then(html => {
                    document.getElementById("profileContent").innerHTML = html;
                    const tempDiv = document.createElement('div');
                    tempDiv.innerHTML = html;
                    const scriptTag = tempDiv.querySelector('script');
                    if (scriptTag) {
                        eval(scriptTag.textContent);
                    }
                });
        });
    }

    function initializeDragAndDrop() {
        $('.draggable-header').draggable({
            helper: 'clone',
            revert: 'invalid',
            appendTo: 'body',
            zIndex: 9999,
            scroll: true,
            containment: 'window',
            opacity: 0.95
        });

        $('#groupingArea').droppable({
            accept: '.draggable-header',
            hoverClass: 'drag-over',
            drop: function(event, ui) {
                const columnName = ui.draggable.data('column');
                const columnText = ui.draggable.text().trim();
                if (currentGrouping.indexOf(columnName) === -1) {
                    addGrouping(columnName, columnText);
                }
            },
            activate: function(){ $('#groupingArea').addClass('drag-over'); },
            deactivate: function(){ $('#groupingArea').removeClass('drag-over'); },
            over: function(){ $('#groupingArea').addClass('drag-over'); },
            out: function(){ $('#groupingArea').removeClass('drag-over'); }
        });
    }

    function wireToolbar() {
        if (!dataTable) return;

        const buttons = dataTable.buttons().container();
        $('#dtButtons').empty().append(buttons);

        $('#studentsSearch')
            .off('input.dtSearch')
            .on('input.dtSearch', function () {
                dataTable.search(this.value).draw();
            });
    }

    function initializeDataTable(groupingConfig = null) {
        if ($.fn.DataTable.isDataTable('#studentsTable')) {
            $('#studentsTable').DataTable().destroy();
        }

        const groupIndices = groupingConfig && groupingConfig.groupIndices ? groupingConfig.groupIndices : [];
        groupSort = new Map();
        groupIndices.forEach(i => groupSort.set(i, 'asc'));

        dataTable = $('#studentsTable').DataTable({
            select: {
                style: 'os',
                items: 'row',
                // Any data cell will toggle the row, but ignore cells we mark as blockers
                selector: 'td:not(.select-blocker)'
            },

            order: lastUserOrder && lastUserOrder.length ? lastUserOrder : [[0, 'asc']],
            orderMulti: true,
            processing: false,
            info: false,
            dom: 'Bfrtip',
            buttons: [
                { extend: 'excelHtml5', text: '<i class="fas fa-file-excel"></i> Excel', className: 'btn btn-success btn-sm', exportOptions: { columns: ':visible' } },
                { extend: 'csvHtml5',   text: '<i class="fas fa-file-csv"></i> CSV',   className: 'btn btn-info btn-sm',    exportOptions: { columns: ':visible' } },
                { extend: 'print',      text: '<i class="fas fa-print"></i> Print',    className: 'btn btn-secondary btn-sm', exportOptions: { columns: ':visible' } },
                {
                    text: '<i class="fas fa-download"></i> Export Selected',
                    className: 'btn btn-primary btn-sm',
                    action: function (e, dt) {
                        dt.button('.buttons-csv', {
                            exportOptions: { modifier: { selected: true } }
                        }).trigger();
                    }
                }
            ],
            orderFixed: groupIndices.length ? { pre: groupIndices.map(i => [i, 'asc']) } : null,
            rowGroup: groupingConfig ? groupingConfig.config : false,
            paging: false,
            pageLength: 50,
            deferRender: true
        });

        // Remember non-group secondary order; don't reorder here
        dataTable
            .off('order.dt.remember')
            .on('order.dt.remember', function () {
                if (!groupIndices.length) {
                    lastUserOrder = dataTable.order();
                    return;
                }
                if (suppressOrderBounce) {
                    suppressOrderBounce = false;
                    return;
                }
                const current = dataTable.order();
                const secondary = current.filter(([idx]) => !groupIndices.includes(idx));
                if (secondary.length) lastUserOrder = secondary;
            });

        // Allow sorting grouped columns by clicking their headers (no recursion)
        $('#studentsTable thead')
            .off('click.groupSort')
            .on('click.groupSort', 'th', function (e) {
                if (!groupIndices.length) return;

                const idx = dataTable.column(this).index();
                if (!groupIndices.includes(idx)) return; // not grouped → let DT handle

                e.preventDefault();
                e.stopPropagation();

                const nextDir = (groupSort.get(idx) === 'asc') ? 'desc' : 'asc';
                groupSort.set(idx, nextDir);

                const fixedPre = groupIndices.map(i => [i, groupSort.get(i) || 'asc']);
                dataTable.order.fixed({ pre: fixedPre });

                suppressOrderBounce = true;
                dataTable.order([...fixedPre, ...lastUserOrder]).draw(false);
            });

        // Prevent row select on profile link (namespaced, idempotent)
        // Don't toggle selection when clicking interactive controls
        $('#studentsTable')
            .off('click.stopSelectOnControls')
            .on('click.stopSelectOnControls', 'a, button, input, label, select, textarea', function (e) {
                e.stopPropagation();
        });

        wireToolbar();
    }

    function setupGroupSelectionEvents() {
        const $table = $('#studentsTable');

        // Ensure we don't stack listeners on re-init
        $table.off('click.selectGroup');

        // Delegate from the stable table element (survives redraws)
        $table.on('click.selectGroup', 'tr.dtrg-start, .clickable-group-header', function (e) {
            const $groupRow = $(this).closest('tr.dtrg-start');
            if (!$groupRow.length) return;

            // All rows until the next group header; exclude group-start/end markers
            const $range = $groupRow.nextUntil('tr.dtrg-start');
            const $dataRows = $range.filter('tr').not('.dtrg-start,.dtrg-end');

            if (!$dataRows.length) return;

            const dtRows = dataTable.rows($dataRows);
            const total = dtRows.count();
            const selected = dtRows.nodes().to$().filter('.selected').length;

            (selected < total) ? dtRows.select() : dtRows.deselect();
        });
    }


    function addGrouping(columnName, columnText) {
        currentGrouping.push(columnName);

        if (currentGrouping.length === 1) {
            $('#groupingArea .ga-placeholder').remove();
            $('.clear-grouping').show();
        }

        const groupTag = $(
            `<span class="group-tag" data-group-for="${columnName}">
                ${columnText}
                <span class="remove" onclick="StudentGrouping.removeGrouping('${columnName}')">&times;</span>
            </span>`
        );
        $('#groupingArea').append(groupTag);

        applyGrouping();
    }

    function removeGrouping(columnName) {
        currentGrouping = currentGrouping.filter(g => g !== columnName);
        $(`.group-tag[data-group-for="${columnName}"]`).remove();
        if (currentGrouping.length === 0) {
            resetGroupingArea();
        }
        applyGrouping();
    }

    function resetGroupingArea() {
        $('#groupingArea')
            .removeClass('drag-over ui-droppable-active ui-state-active ui-state-hover')
            .css('cursor', 'auto')
            .empty()
            .append('<div class="ga-placeholder">Drag column headers here to group students by that field</div>');
        $('.clear-grouping').hide();
    }

    function clearGrouping() {
        currentGrouping = [];
        resetGroupingArea();
        applyGrouping();

        // Reset any lingering “busy” cursor
        $('body, #studentsTable, .dataTables_wrapper').css('cursor', 'auto');
        $('.dataTables_processing').hide();
    }

    function applyGrouping() {
        if (!currentGrouping.length) {
            initializeDataTable(null);
            return;
        }

        const groupIndices = currentGrouping.map(name => columnMap.get(name));
        groupSort = new Map();
        groupIndices.forEach(i => groupSort.set(i, 'asc'));

        const groupingConfig = {
            groupIndices,
            config: {
                dataSrc: groupIndices,
                startRender: function (rows, group, level) {
                    const columnIndex = groupIndices[level];
                    const columnHeader = $('#studentsTable thead th').eq(columnIndex).text().trim();
                    const displayText = `${columnHeader}: ${group} (${rows.count()} students)`;
                    // Return only the cell content; RowGroup builds the <tr>/<td> with dtrg-start
                    return `<span class="clickable-group-header">${displayText}</span>`;
                }
            }
        };
        initializeDataTable(groupingConfig);
    }

    function loadJQueryUI() {
        if (!$('link[href*="jquery-ui"]').length) {
            $('<link rel="stylesheet" href="https://code.jquery.com/ui/1.13.2/themes/ui-lightness/jquery-ui.css">').appendTo('head');
        }
    }

    return {
        init: init,
        addGrouping: addGrouping,
        removeGrouping: removeGrouping,
        clearGrouping: clearGrouping
    };
})();

// Charts functionality (Unchanged)
window.StudentCharts = (function() {
    function init() {
        if (window.studentPageData) {
            initializeIndicatorChart();
            initializeQuadrantChart();
        }
    }

    function initializeIndicatorChart() {
        const indicatorData = window.studentPageData.indicatorData;
        const indicatorLabels = indicatorData.map(i => i.Name);
        const indicatorPercents = indicatorData.map(i => i.PercentMet.toFixed(1));
        if (indicatorData.length > 0) {
            if (window.indicatorChartInstance) {
                window.indicatorChartInstance.destroy();
            }
            window.indicatorChartInstance = new Chart(document.getElementById('indicatorChart'), {
                type: 'bar',
                data: {
                    labels: indicatorLabels,
                    datasets: [{
                        label: '% Met',
                        data: indicatorPercents,
                        backgroundColor: '#0d6efd'
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    scales: { y: { beginAtZero: true, max: 100, title: { display: true, text: '% Met' } } },
                    plugins: { legend: { display: false } }
                }
            });
        }
    }

    function initializeQuadrantChart() {
        const data = window.studentPageData;
        const quadrantCounts = data.quadrantCounts;
        const total = data.total;
        new Chart(document.getElementById('quadrantPie'), {
            type: 'pie',
            data: {
                labels: [
                    `Challenge (${quadrantCounts["Challenge"] || 0})`,
                    `Benchmark (${quadrantCounts["Benchmark"] || 0})`,
                    `Strategic (${quadrantCounts["Strategic"] || 0})`,
                    `Intensive (${quadrantCounts["Intensive"] || 0})`
                ],
                datasets: [{
                    data: [
                        quadrantCounts["Challenge"] || 0,
                        quadrantCounts["Benchmark"] || 0,
                        quadrantCounts["Strategic"] || 0,
                        quadrantCounts["Intensive"] || 0
                    ],
                    backgroundColor: ['#007bff', '#28a745', '#ffc107', '#dc3545'],
                    borderColor: '#fff',
                    borderWidth: 1
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: true,
                plugins: {
                    legend: { display: true, position: 'top', align: 'center' },
                    title: { display: false },
                    tooltip: {
                        callbacks: {
                            label: function(context) {
                                const value = context.parsed;
                                const percent = total > 0 ? ((value / total) * 100).toFixed(1) : 0;
                                return `${context.label}: ${value} (${percent}%)`;
                            }
                        }
                    }
                }
            }
        });
    }

    return {
        init: init
    };
})();

// Global functions for onclick handlers
window.clearGrouping = function() {
    StudentGrouping.clearGrouping();
};
