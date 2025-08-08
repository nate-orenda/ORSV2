// Students Grouping and Table Management
window.StudentGrouping = (function() {
    let currentGrouping = [];
    let dataTable = null;
    const columnMap = new Map();

    function init() {
        buildColumnMap();
        initializeModal();
        initializeDataTable();       // <-- move this BEFORE drag
        initializeDragAndDrop();     // <-- bind after DT builds thead
        setupGroupSelectionEvents();
        loadJQueryUI();
    }

    function buildColumnMap() {
        $('#studentsTable thead th').each(function() {
            const columnName = $(this).data('column');
            if (columnName) {
                const columnIndex = $(this).index();
                columnMap.set(columnName, columnIndex);
            }
        });
    }

    function initializeModal() {
        var modal = document.getElementById('profileModal');
        modal.addEventListener('show.bs.modal', function(event) {
            var button = event.relatedTarget;
            var studentId = button.getAttribute('data-student-id');
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

    function initializeDragAndDrop(rebind = false) {
  if (rebind) {
    try { $('.draggable-header').draggable('destroy'); } catch {}
  }

  $('.draggable-header').draggable({
    helper: function () {
      const $th = $(this).clone();
      $th
        .css({ width: $(this).outerWidth(), 'pointer-events': 'none', 'z-index': 200000 })
        .addClass('drag-helper');
      return $th;
    },
    appendTo: 'body',
    zIndex: 200000,
    revert: 'invalid',
    scroll: false,
    containment: 'window',
    opacity: 0.95,
    distance: 3
  });

  $('#groupingArea').droppable({
    accept: '.draggable-header',
    tolerance: 'pointer',      // important for reliable drop in prod
    greedy: true,
    hoverClass: 'drag-over',
    drop: function (event, ui) {
      const columnName = ui.draggable.data('column');
      const columnText = ui.draggable.text().trim();
      if (!currentGrouping.includes(columnName)) addGrouping(columnName, columnText);
    }
  });
}


    function moveButtonsToToolbar() {
  const $target = $('#dtButtons');
  if (!$target.length) return;
  if (!dataTable || !dataTable.buttons || !dataTable.buttons().container) return;

  const $container = $(dataTable.buttons().container());
  if (!$container.length) return;

  if (!$container.parent().is($target)) {
    $container.detach().appendTo($target);
  }
}

function wireToolbar() {
  moveButtonsToToolbar();
  setTimeout(moveButtonsToToolbar, 0);
  setTimeout(moveButtonsToToolbar, 100);
  setTimeout(moveButtonsToToolbar, 300);
}

function watchButtonsWrapper() {
  const wrapper = $('#studentsTable').closest('.dataTables_wrapper')[0];
  if (!wrapper || wrapper.__dtObserver) return;
  const obs = new MutationObserver(() => moveButtonsToToolbar());
  obs.observe(wrapper, { childList: true, subtree: true });
  wrapper.__dtObserver = obs;
}

    function initializeDataTable(groupingConfig = false) {
  const $table = $('#studentsTable');
  if ($.fn.DataTable.isDataTable($table)) $table.DataTable().destroy();

  dataTable = $table.DataTable({
    dom: 'Bfrtip',
    buttons: [
      { extend: 'excelHtml5', className: 'btn btn-success btn-sm', exportOptions: { columns: ':visible' } },
      { extend: 'csvHtml5',   className: 'btn btn-info btn-sm',    exportOptions: { columns: ':visible' } },
      { extend: 'print',      className: 'btn btn-secondary btn-sm', exportOptions: { columns: ':visible' } },
      {
        text: 'Export Selected',
        className: 'btn btn-primary btn-sm',
        action: function (e, dt) {
          dt.button('.buttons-csv', { exportOptions: { modifier: { selected: true } } }).trigger();
        }
      }
    ],
            rowGroup: groupingConfig ? groupingConfig.config : false,
            paging: false,
            pageLength: 50,
            deferRender: true
        });

       // Buttons wiring you already added
  $table.off('.dtToolbar');
  $table.on('init.dt.dtToolbar draw.dt.dtToolbar', wireToolbar);
  watchButtonsWrapper();
  wireToolbar();

  // Rebind draggables after DT (re)renders header
  $table.off('init.dt.drag draw.dt.drag');
  $table.on('init.dt.drag draw.dt.drag', function () {
    initializeDragAndDrop(true);  // pass true to force rebind
  });
}


    function setupGroupSelectionEvents() {
        $('#studentsTable tbody').on('click', '.clickable-group-header', function() {
            const groupRow = $(this).closest('tr.dtrg-start');
            const childRows = groupRow.nextUntil('tr.dtrg-start');
            const groupDtRows = dataTable.rows(childRows);
            const selectedInGroupCount = groupDtRows.nodes().to$().filter('.selected').length;
            const totalInGroupCount = groupDtRows.count();
            const shouldSelect = selectedInGroupCount < totalInGroupCount;
            if (shouldSelect) {
                groupDtRows.select();
            } else {
                groupDtRows.deselect();
            }
        });
    }

    function addGrouping(columnName, columnText) {
        currentGrouping.push(columnName);
        
        if (currentGrouping.length === 1) {
            $('#groupingArea .placeholder').remove(); 
            $('.clear-grouping').show();
        }
        
        var groupTag = $(`<span class="group-tag" data-group-for="${columnName}">${columnText}<span class="remove" onclick="StudentGrouping.removeGrouping('${columnName}')">&times;</span></span>`);
        
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
        $('#groupingArea').empty().append('<div class="placeholder">Drag column headers here to group students by that field</div>');
        $('.clear-grouping').hide();
    }

    function clearGrouping() {
        currentGrouping = [];
        resetGroupingArea();
        applyGrouping();
    }

    function applyGrouping() {
        if (!currentGrouping.length) {
            initializeDataTable(false);
            return;
        }
        const groupIndices = currentGrouping.map(name => columnMap.get(name));
        const groupingConfig = {
            config: {
                dataSrc: groupIndices,
                startRender: function(rows, group, level) {
                    const columnIndex = groupIndices[level];
                    const columnHeader = $('#studentsTable thead th').eq(columnIndex).text().trim();
                    const displayText = `${columnHeader}: ${group} (${rows.count()} students)`;
                    const cellContent = `<span class="clickable-group-header">${displayText}</span>`;
                    return $('<tr/>').append(`<td colspan="${rows.columns().nodes().length}">${cellContent}</td>`);
                }
            },
            order: groupIndices.map(index => [index, 'asc'])
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
