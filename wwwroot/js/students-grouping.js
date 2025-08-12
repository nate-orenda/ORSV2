// Students Grouping and Table Management
window.StudentGrouping = (function() {
    // Private variables for module state
    let currentGrouping = [];
    let dataTable = null;
    const columnMap = new Map();
    let lastUserOrder = [[0, 'asc']];
    let groupSort = new Map();
    let suppressOrderBounce = false;
    let schoolId = null;
    let grade = null; // NEW: Variable to hold the grade

    /**
     * Initializes the entire student grouping module.
     * @param {object} config - Configuration object.
     * @param {number} config.schoolId - The ID of the current school.
     * @param {number} config.grade - The current grade level.
     */
    function init(config) {
        if (!config || !config.schoolId || !config.grade) {
            console.error("StudentGrouping init requires a schoolId and grade.");
            return;
        }
        schoolId = config.schoolId; // Store the school ID
        grade = config.grade;       // Store the grade

        injectCompactStyles();
        buildColumnMap();
        initializeModal();
        initializeDragAndDrop();
        initializeDataTable();
        setupGroupSelectionEvents();
        loadJQueryUI();
    }

    /**
     * Injects CSS for a more compact table layout.
     */
    function injectCompactStyles() {
        const styleId = 'datatable-compact-styles';
        if (document.getElementById(styleId)) return;

        const css = `
            #studentsTable td, #studentsTable th { padding: 4px 8px !important; white-space: nowrap; }
            .btn.condensed-btn { padding: 0.15rem 0.4rem; font-size: 0.8rem; }
            .narrow-column { width: 50px; }`;

        const style = document.createElement('style');
        style.id = styleId;
        style.type = 'text/css';
        style.appendChild(document.createTextNode(css));
        document.head.appendChild(style);
    }

    /**
     * Maps column names from data attributes to their index.
     */
    function buildColumnMap() {
        $('#studentsTable thead th').each(function() {
            const columnName = $(this).data('column');
            if (columnName) {
                const columnIndex = $(this).index();
                columnMap.set(columnName, columnIndex);
            }
        });
    }

    /**
     * Sets up the student profile modal.
     */
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

    /**
     * Initializes jQuery UI drag and drop for table headers.
     */
    function initializeDragAndDrop() {
        $('.draggable-header').draggable({
            helper: 'clone',
            revert: 'invalid',
            appendTo: 'body',
            zIndex: 9999
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
            }
        });
    }

    /**
     * Wires up the DataTable buttons and search input to the toolbar.
     */
    function wireToolbar() {
        if (!dataTable) return;
        const buttons = dataTable.buttons().container();
        $('#dtButtons').empty().append(buttons);
        $('#studentsSearch').off('input.dtSearch').on('input.dtSearch', function () {
            dataTable.search(this.value).draw();
        });
    }

    /**
     * Binds all events related to the 'Create Target Group' button.
     * This function is called every time the table is re-initialized.
     */
    function bindTargetGroupEvents() {
        if (!window.studentsTable) return;

        const table = window.studentsTable;
        const createBtn = $('#createTargetGroupBtn');

        table.off('select.targetGroup deselect.targetGroup').on('select.targetGroup deselect.targetGroup', function () {
            const selectedRows = table.rows({ selected: true }).count();
            createBtn.prop('disabled', selectedRows === 0);
        });

        createBtn.off('click.targetGroup').on('click.targetGroup', function () {
            const studentResultIds = [];
            table.rows({ selected: true }).nodes().each(function (rowNode) {
                const resultId = parseInt($(rowNode).find('.view-profile').attr('data-student-id'), 10);
                if (resultId) {
                    studentResultIds.push(parseInt(resultId));
                }
            });

            if (studentResultIds.length === 0) {
                alert("No students selected or could not find student IDs.");
                return;
            }

            const groupName = prompt(`Enter a name for the new target group (${studentResultIds.length} students):`);

            if (groupName && groupName.trim() !== "") {
                createGroup(groupName.trim(), studentResultIds);
            }
        });
    }
    
    /**
     * Sends the data to the server to create a new target group.
     * @param {string} name - The name for the new group.
     * @param {number[]} studentIds - An array of student GAResult IDs.
     */
    function createGroup(name, studentIds) {
        const verificationToken = $('input[name="__RequestVerificationToken"]').val();
        
        // FIX: Construct the correct URL with route parameters
        const url = `${window.location.pathname}?handler=CreateTargetGroup`;

        fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': verificationToken
            },
            body: JSON.stringify({
                groupName: name,
                studentResultIds: studentIds,
                schoolId: schoolId
            })
        })
        .then(response => {
            if (!response.ok) {
                return response.text().then(text => { 
                    throw new Error(text || `Server returned status ${response.status}`); 
                });
            }
            return response.json();
        })
        .then(data => {
            if (data.newGroupId) {
                alert('Successfully created target group!');
                window.location.href = `/GuidanceAlignment/TargetGroup/${data.newGroupId}`;
            }
        })
        .catch(error => {
            console.error('Error creating target group:', error);
            alert(`Error: ${error.message}`);
        });
    }

    /**
     * Initializes or re-initializes the main DataTable with optional grouping.
     * @param {object|null} groupingConfig - Configuration for grouping, if any.
     */
    function initializeDataTable(groupingConfig = null) {
        if ($.fn.DataTable.isDataTable('#studentsTable')) {
            $('#studentsTable').DataTable().destroy();
        }

        const groupIndices = groupingConfig?.groupIndices || [];
        groupSort = new Map();
        groupIndices.forEach(i => groupSort.set(i, 'asc'));

        dataTable = window.studentsTable = $('#studentsTable').DataTable({
            select: { style: 'os', items: 'row', selector: 'td:not(.select-blocker)' },
            order: lastUserOrder.length ? lastUserOrder : [[0, 'asc']],
            orderMulti: true,
            processing: false,
            info: false,
            dom: 'Bfrtip',
            buttons: [
                { extend: 'excelHtml5', text: '<i class="fas fa-file-excel"></i> Excel', className: 'btn btn-success btn-sm condensed-btn', exportOptions: { columns: ':visible' } },
                { extend: 'csvHtml5',   text: '<i class="fas fa-file-csv"></i> CSV',   className: 'btn btn-info btn-sm condensed-btn',    exportOptions: { columns: ':visible' } },
                { extend: 'print',      text: '<i class="fas fa-print"></i> Print',   className: 'btn btn-secondary btn-sm condensed-btn', exportOptions: { columns: ':visible' } },
                {
                    text: '<i class="fas fa-download"></i> Export Selected',
                    className: 'btn btn-primary btn-sm condensed-btn',
                    action: function (e, dt) {
                        dt.button('.buttons-csv', { exportOptions: { modifier: { selected: true } } }).trigger();
                    }
                }
            ],
            columnDefs: [{ "targets": [0], "className": "narrow-column" }],
            orderFixed: groupIndices.length ? { pre: groupIndices.map(i => [i, 'asc']) } : null,
            rowGroup: groupingConfig ? groupingConfig.config : false,
            paging: false,
            deferRender: true
        });

        dataTable.off('order.dt.remember').on('order.dt.remember', function () {
            if (!groupIndices.length) { lastUserOrder = dataTable.order(); return; }
            if (suppressOrderBounce) { suppressOrderBounce = false; return; }
            const current = dataTable.order();
            const secondary = current.filter(([idx]) => !groupIndices.includes(idx));
            if (secondary.length) lastUserOrder = secondary;
        });

        $('#studentsTable thead').off('click.groupSort').on('click.groupSort', 'th', function (e) {
            if (!groupIndices.length) return;
            const idx = dataTable.column(this).index();
            if (!groupIndices.includes(idx)) return;
            e.preventDefault();
            e.stopPropagation();
            const nextDir = (groupSort.get(idx) === 'asc') ? 'desc' : 'asc';
            groupSort.set(idx, nextDir);
            const fixedPre = groupIndices.map(i => [i, groupSort.get(i) || 'asc']);
            dataTable.order.fixed({ pre: fixedPre });
            suppressOrderBounce = true;
            dataTable.order([...fixedPre, ...lastUserOrder]).draw(false);
        });

        $('#studentsTable').off('click.stopSelectOnControls').on('click.stopSelectOnControls', 'a, button, input, label, select, textarea', function (e) {
            e.stopPropagation();
        });

        wireToolbar();
        bindTargetGroupEvents();
    }

    function setupGroupSelectionEvents() {
        const $table = $('#studentsTable');
        $table.off('click.selectGroup').on('click.selectGroup', 'tr.dtrg-start, .clickable-group-header', function () {
            const $groupRow = $(this).closest('tr.dtrg-start');
            if (!$groupRow.length) return;
            const $dataRows = $groupRow.nextUntil('tr.dtrg-start').filter('tr').not('.dtrg-start,.dtrg-end');
            if (!$dataRows.length) return;
            const dtRows = dataTable.rows($dataRows);
            const isAllSelected = dtRows.nodes().to$().filter('.selected').length === dtRows.count();
            isAllSelected ? dtRows.deselect() : dtRows.select();
        });
    }

    function addGrouping(columnName, columnText) {
        currentGrouping.push(columnName);
        if (currentGrouping.length === 1) {
            $('#groupingArea .ga-placeholder').remove();
            $('.clear-grouping').show();
        }
        const groupTag = $(`<span class="group-tag" data-group-for="${columnName}">${columnText}<span class="remove" onclick="StudentGrouping.removeGrouping('${columnName}')">&times;</span></span>`);
        $('#groupingArea').append(groupTag);
        applyGrouping();
    }

    function removeGrouping(columnName) {
        currentGrouping = currentGrouping.filter(g => g !== columnName);
        $(`.group-tag[data-group-for="${columnName}"]`).remove();
        if (currentGrouping.length === 0) { resetGroupingArea(); }
        applyGrouping();
    }

    function resetGroupingArea() {
        $('#groupingArea').empty().append('<div class="ga-placeholder">Drag column headers here to group students by that field</div>');
        $('.clear-grouping').hide();
    }

    function clearGrouping() {
        currentGrouping = [];
        resetGroupingArea();
        applyGrouping();
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
                    return `<span class="clickable-group-header">${columnHeader}: ${group} (${rows.count()} students)</span>`;
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
                    datasets: [{ label: '% Met', data: indicatorPercents, backgroundColor: '#0d6efd' }]
                },
                options: {
                    responsive: true, maintainAspectRatio: false,
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
                labels: [`Challenge (${quadrantCounts["Challenge"] || 0})`, `Benchmark (${quadrantCounts["Benchmark"] || 0})`, `Strategic (${quadrantCounts["Strategic"] || 0})`, `Intensive (${quadrantCounts["Intensive"] || 0})`],
                datasets: [{
                    data: [quadrantCounts["Challenge"] || 0, quadrantCounts["Benchmark"] || 0, quadrantCounts["Strategic"] || 0, quadrantCounts["Intensive"] || 0],
                    backgroundColor: ['#007bff', '#28a745', '#ffc107', '#dc3545'],
                    borderColor: '#fff',
                    borderWidth: 1
                }]
            },
            options: {
                responsive: true, maintainAspectRatio: true,
                plugins: {
                    legend: { display: true, position: 'top', align: 'center' },
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

    return { init: init };
})();

window.clearGrouping = function() {
    StudentGrouping.clearGrouping();
};
