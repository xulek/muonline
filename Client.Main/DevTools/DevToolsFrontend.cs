#if DEBUG
namespace Client.Main.DevTools
{
    /// <summary>
    /// Embedded HTML frontend for DevTools profiler.
    /// Uses CDN-loaded D3.js for visualizations.
    /// </summary>
    public static class DevToolsFrontend
    {
        public const string Html = @"<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>MuOnline Profiler</title>
    <script src='https://d3js.org/d3.v7.min.js'></script>
    <style>
        :root {
            --bg-0: #0a0a0b;
            --bg-1: #111113;
            --bg-2: #18181b;
            --bg-3: #1f1f23;
            --bg-4: #27272b;
            --border: #2e2e33;
            --border-light: #3f3f46;
            --text-0: #fafafa;
            --text-1: #a1a1aa;
            --text-2: #71717a;
            --accent: #3b82f6;
            --accent-hover: #2563eb;
            --success: #22c55e;
            --warning: #eab308;
            --danger: #ef4444;
            --radius: 8px;
        }
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body {
            font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
            background: var(--bg-0);
            color: var(--text-0);
            font-size: 13px;
            line-height: 1.5;
            height: 100vh;
            overflow: hidden;
        }

        /* Layout */
        .app {
            display: grid;
            grid-template-rows: 56px 1fr;
            grid-template-columns: 1fr 340px;
            height: 100vh;
        }

        /* Header */
        .header {
            grid-column: 1 / -1;
            background: var(--bg-1);
            border-bottom: 1px solid var(--border);
            display: flex;
            align-items: center;
            padding: 0 20px;
            gap: 20px;
        }
        .logo {
            display: flex;
            align-items: center;
            gap: 10px;
            font-weight: 600;
            font-size: 15px;
        }
        .logo-icon {
            width: 28px;
            height: 28px;
            background: linear-gradient(135deg, var(--accent) 0%, #8b5cf6 100%);
            border-radius: 6px;
            display: flex;
            align-items: center;
            justify-content: center;
            font-size: 14px;
        }

        /* Navigation */
        .nav {
            display: flex;
            gap: 4px;
            background: var(--bg-2);
            padding: 4px;
            border-radius: 10px;
        }
        .nav-item {
            padding: 8px 16px;
            border: none;
            background: transparent;
            color: var(--text-1);
            font-size: 13px;
            font-weight: 500;
            cursor: pointer;
            border-radius: 6px;
            transition: all 0.15s ease;
            display: flex;
            align-items: center;
            gap: 8px;
        }
        .nav-item:hover { color: var(--text-0); background: var(--bg-3); }
        .nav-item.active { background: var(--accent); color: white; }
        .nav-badge {
            background: rgba(255,255,255,0.15);
            padding: 2px 7px;
            border-radius: 100px;
            font-size: 11px;
            font-weight: 600;
        }

        /* Header Controls */
        .header-controls {
            display: flex;
            gap: 8px;
            margin-left: auto;
        }
        .btn {
            padding: 8px 14px;
            border: 1px solid var(--border);
            border-radius: 6px;
            background: var(--bg-2);
            color: var(--text-0);
            font-size: 12px;
            font-weight: 500;
            cursor: pointer;
            transition: all 0.15s ease;
            display: flex;
            align-items: center;
            gap: 6px;
        }
        .btn:hover { background: var(--bg-3); border-color: var(--border-light); }
        .btn.active, .btn.recording { background: var(--danger); border-color: var(--danger); }
        .status-badge {
            padding: 6px 12px;
            border-radius: 100px;
            font-size: 11px;
            font-weight: 600;
            text-transform: uppercase;
            letter-spacing: 0.5px;
        }
        .status-badge.connected { background: rgba(34, 197, 94, 0.15); color: var(--success); }
        .status-badge.disconnected { background: rgba(239, 68, 68, 0.15); color: var(--danger); }

        /* Main Content */
        .main {
            display: flex;
            flex-direction: column;
            overflow: hidden;
            background: var(--bg-0);
        }

        /* Tab Views */
        .tab-view { display: none; flex-direction: column; height: 100%; overflow: hidden; }
        .tab-view.active { display: flex; }

        /* Dashboard */
        .metrics-row {
            display: flex;
            gap: 12px;
            padding: 16px 20px;
            background: var(--bg-1);
            border-bottom: 1px solid var(--border);
        }
        .metric-card {
            flex: 1;
            background: var(--bg-2);
            border: 1px solid var(--border);
            border-radius: var(--radius);
            padding: 16px;
            text-align: center;
        }
        .metric-value {
            font-size: 28px;
            font-weight: 700;
            font-variant-numeric: tabular-nums;
            line-height: 1.2;
        }
        .metric-label {
            font-size: 11px;
            color: var(--text-2);
            text-transform: uppercase;
            letter-spacing: 0.5px;
            margin-top: 4px;
        }
        .metric-card.success .metric-value { color: var(--success); }
        .metric-card.warning .metric-value { color: var(--warning); }
        .metric-card.danger .metric-value { color: var(--danger); }

        .chart-section {
            flex: 1;
            padding: 20px;
            overflow: hidden;
        }
        .chart-card {
            height: 100%;
            background: var(--bg-1);
            border: 1px solid var(--border);
            border-radius: var(--radius);
            padding: 16px;
            display: flex;
            flex-direction: column;
        }
        .chart-header {
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 12px;
        }
        .chart-title {
            font-size: 14px;
            font-weight: 600;
        }
        .chart-meta {
            display: flex;
            align-items: center;
            gap: 8px;
            color: var(--text-2);
            font-size: 12px;
        }
        .selection-badge {
            padding: 4px 8px;
            border-radius: 999px;
            background: var(--bg-3);
            border: 1px solid var(--border);
            color: var(--text-1);
            font-size: 11px;
            font-weight: 600;
        }
        .selection-badge.pinned-visible {
            background: rgba(245, 158, 11, 0.15);
            border-color: #f59e0b;
            color: #f59e0b;
        }
        .selection-badge.pinned-historic {
            background: rgba(245, 158, 11, 0.08);
            border-color: rgba(245, 158, 11, 0.4);
            color: rgba(245, 158, 11, 0.7);
        }
        .btn.small {
            padding: 6px 10px;
            font-size: 11px;
        }
        .chart-body { flex: 1; min-height: 0; }
        .chart-body svg { width: 100%; height: 100%; }

        /* Hierarchy Tab */
        .tab-toolbar {
            display: flex;
            align-items: center;
            gap: 12px;
            padding: 12px 20px;
            background: var(--bg-1);
            border-bottom: 1px solid var(--border);
        }
        .search-input {
            flex: 1;
            max-width: 320px;
            padding: 10px 14px;
            background: var(--bg-2);
            border: 1px solid var(--border);
            border-radius: 6px;
            color: var(--text-0);
            font-size: 13px;
        }
        .search-input:focus {
            outline: none;
            border-color: var(--accent);
            box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.15);
        }
        .search-input::placeholder { color: var(--text-2); }

        .hierarchy-scroll {
            flex: 1;
            overflow-y: auto;
            padding: 16px 20px;
        }

        /* Tree Nodes */
        .tree-node {
            margin: 2px 0;
        }
        .tree-row {
            display: flex;
            align-items: center;
            gap: 8px;
            padding: 10px 12px;
            background: var(--bg-1);
            border: 1px solid var(--border);
            border-radius: 6px;
            cursor: pointer;
            transition: all 0.1s ease;
        }
        .tree-row:hover {
            background: var(--bg-2);
            border-color: var(--border-light);
        }
        .tree-toggle {
            width: 20px;
            height: 20px;
            display: flex;
            align-items: center;
            justify-content: center;
            color: var(--text-2);
            font-size: 10px;
            border-radius: 4px;
            flex-shrink: 0;
        }
        .tree-toggle:hover { background: var(--bg-3); color: var(--text-0); }
        .tree-toggle.empty { visibility: hidden; }
        .tree-info { flex: 1; min-width: 0; }
        .tree-name {
            font-weight: 500;
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
        }
        .tree-meta {
            font-size: 11px;
            color: var(--text-2);
            margin-top: 2px;
        }
        .tree-timing {
            font-size: 12px;
            font-weight: 600;
            font-variant-numeric: tabular-nums;
            padding: 4px 10px;
            border-radius: 100px;
            background: var(--bg-3);
            flex-shrink: 0;
        }
        .tree-timing.fast { color: var(--success); background: rgba(34, 197, 94, 0.1); }
        .tree-timing.medium { color: var(--warning); background: rgba(234, 179, 8, 0.1); }
        .tree-timing.slow { color: var(--danger); background: rgba(239, 68, 68, 0.1); }
        .tree-children {
            margin-left: 24px;
            padding-left: 16px;
            border-left: 2px solid var(--border);
            margin-top: 4px;
        }
        .tree-node.collapsed > .tree-children { display: none; }

        /* Hotspots Tab */
        .hotspots-grid {
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(340px, 1fr));
            gap: 12px;
            padding: 20px;
            overflow-y: auto;
        }
        .hotspot-card {
            background: var(--bg-1);
            border: 1px solid var(--border);
            border-radius: var(--radius);
            padding: 16px;
            cursor: pointer;
            transition: all 0.15s ease;
        }
        .hotspot-card:hover {
            background: var(--bg-2);
            border-color: var(--border-light);
            transform: translateY(-1px);
        }
        .hotspot-card.selected {
            border-color: var(--accent);
            box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.15);
        }
        .hotspot-header {
            display: flex;
            align-items: flex-start;
            gap: 12px;
            margin-bottom: 12px;
        }
        .hotspot-rank {
            width: 28px;
            height: 28px;
            background: var(--bg-3);
            border-radius: 6px;
            display: flex;
            align-items: center;
            justify-content: center;
            font-weight: 700;
            font-size: 12px;
            flex-shrink: 0;
        }
        .hotspot-rank.top { background: var(--danger); color: white; }
        .hotspot-title { flex: 1; min-width: 0; }
        .hotspot-name {
            font-weight: 600;
            font-size: 14px;
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
        }
        .hotspot-type {
            font-size: 12px;
            color: var(--text-2);
            margin-top: 2px;
        }
        .hotspot-time {
            font-size: 18px;
            font-weight: 700;
            font-variant-numeric: tabular-nums;
        }
        .hotspot-time.slow { color: var(--danger); }
        .hotspot-time.medium { color: var(--warning); }
        .hotspot-time.fast { color: var(--success); }
        .hotspot-bars {
            display: flex;
            gap: 8px;
            margin-top: 12px;
        }
        .hotspot-bar {
            flex: 1;
            text-align: center;
        }
        .hotspot-bar-label {
            font-size: 10px;
            color: var(--text-2);
            text-transform: uppercase;
            margin-bottom: 4px;
        }
        .hotspot-bar-track {
            height: 6px;
            background: var(--bg-3);
            border-radius: 3px;
            overflow: hidden;
        }
        .hotspot-bar-fill {
            height: 100%;
            border-radius: 3px;
        }
        .hotspot-bar-fill.update { background: var(--accent); }
        .hotspot-bar-fill.draw { background: #8b5cf6; }
        .hotspot-bar-value {
            font-size: 11px;
            font-weight: 600;
            margin-top: 4px;
            font-variant-numeric: tabular-nums;
        }

        /* Right Panel */
        .panel {
            background: var(--bg-1);
            border-left: 1px solid var(--border);
            display: flex;
            flex-direction: column;
            overflow: hidden;
        }
        .panel-header {
            padding: 16px 20px;
            border-bottom: 1px solid var(--border);
        }
        .panel-title {
            font-size: 13px;
            font-weight: 600;
            text-transform: uppercase;
            letter-spacing: 0.5px;
            color: var(--text-1);
        }
        .panel-scroll {
            flex: 1;
            overflow-y: auto;
            padding: 12px;
        }
        .subsystem-card {
            background: var(--bg-2);
            border: 1px solid var(--border);
            border-radius: var(--radius);
            padding: 14px;
            margin-bottom: 10px;
        }
        .subsystem-header {
            font-weight: 600;
            font-size: 13px;
            margin-bottom: 10px;
            display: flex;
            align-items: center;
            gap: 8px;
        }
        .subsystem-icon {
            width: 24px;
            height: 24px;
            background: var(--bg-3);
            border-radius: 5px;
            display: flex;
            align-items: center;
            justify-content: center;
            font-size: 12px;
        }
        .subsystem-row {
            display: flex;
            justify-content: space-between;
            align-items: center;
            padding: 6px 0;
            border-bottom: 1px solid var(--border);
        }
        .subsystem-row:last-child { border-bottom: none; }
        .subsystem-label { color: var(--text-2); font-size: 12px; }
        .subsystem-value {
            font-weight: 600;
            font-variant-numeric: tabular-nums;
            font-size: 13px;
        }

        /* Resizer */
        .panel-resizer {
            width: 4px;
            background: transparent;
            cursor: col-resize;
            transition: background 0.15s;
        }
        .panel-resizer:hover, .panel-resizer.dragging { background: var(--accent); }

        /* Profiler overhead */
        .overhead-badge {
            background: rgba(234, 179, 8, 0.15);
            color: var(--warning);
            padding: 6px 10px;
            border-radius: 6px;
            font-size: 11px;
            font-weight: 500;
        }

        /* Flame Graph */
        .flame-container {
            flex: 1;
            overflow: hidden;
            padding: 16px;
            background: var(--bg-0);
        }
        .flame-container svg {
            width: 100%;
            height: 100%;
        }
        .flame-rect {
            cursor: pointer;
            transition: opacity 0.1s;
        }
        .flame-rect:hover {
            opacity: 0.85;
        }
        .flame-text {
            pointer-events: none;
            fill: white;
            font-size: 11px;
            font-weight: 500;
            text-shadow: 0 1px 2px rgba(0,0,0,0.5);
        }
        .flame-tooltip {
            position: fixed;
            background: var(--bg-3);
            border: 1px solid var(--border-light);
            border-radius: 6px;
            padding: 10px 14px;
            font-size: 12px;
            pointer-events: none;
            z-index: 1000;
            display: none;
            max-width: 300px;
            box-shadow: 0 4px 12px rgba(0,0,0,0.3);
        }
        .flame-tooltip.visible { display: block; }
        .flame-tooltip-name { font-weight: 600; margin-bottom: 4px; }
        .flame-tooltip-type { color: var(--text-2); font-size: 11px; margin-bottom: 8px; }
        .flame-tooltip-row { display: flex; justify-content: space-between; gap: 20px; }
        .flame-tooltip-label { color: var(--text-2); }
        .flame-tooltip-value { font-weight: 600; font-variant-numeric: tabular-nums; }
    </style>
</head>
<body>
    <div class='app'>
        <header class='header'>
            <div class='logo'>
                <div class='logo-icon'>P</div>
                <span>MuOnline Profiler</span>
            </div>
            <nav class='nav'>
                <button class='nav-item active' data-tab='dashboard'>Dashboard</button>
                <button class='nav-item' data-tab='flame'>Flame Graph</button>
                <button class='nav-item' data-tab='hierarchy'>Hierarchy <span class='nav-badge' id='hierarchyCount'>0</span></button>
                <button class='nav-item' data-tab='hotspots'>Hotspots <span class='nav-badge' id='hotspotsCount'>0</span></button>
                <button class='nav-item' data-tab='scopes'>Scopes <span class='nav-badge' id='scopesCount'>0</span></button>
            </nav>
            <div class='header-controls'>
                <button class='btn' id='btnRecord'><span>●</span> Record</button>
                <button class='btn' id='btnPause'><span>❚❚</span> Pause</button>
                <button class='btn' id='btnExport'><span>↓</span> Export</button>
                <span id='overhead' class='overhead-badge' style='display:none'></span>
                <span id='status' class='status-badge disconnected'>Disconnected</span>
            </div>
        </header>

        <main class='main'>
            <!-- Dashboard -->
            <div class='tab-view active' id='tab-dashboard'>
                <div class='metrics-row'>
                    <div class='metric-card' id='metricFps'>
                        <div class='metric-value'>--</div>
                        <div class='metric-label'>FPS</div>
                    </div>
                    <div class='metric-card' id='metricFrame'>
                        <div class='metric-value'>--</div>
                        <div class='metric-label'>Frame Time</div>
                    </div>
                    <div class='metric-card' id='metricUpdate'>
                        <div class='metric-value'>--</div>
                        <div class='metric-label'>Update</div>
                    </div>
                    <div class='metric-card' id='metricDraw'>
                        <div class='metric-value'>--</div>
                        <div class='metric-label'>Draw</div>
                    </div>
                    <div class='metric-card' id='metricBudget'>
                        <div class='metric-value'>--</div>
                        <div class='metric-label'>Budget</div>
                    </div>
                </div>
                <div class='metrics-row'>
                    <div class='metric-card' id='metricCpu'>
                        <div class='metric-value'>--</div>
                        <div class='metric-label'>CPU</div>
                    </div>
                    <div class='metric-card' id='metricGpu'>
                        <div class='metric-value'>--</div>
                        <div class='metric-label'>GPU</div>
                    </div>
                    <div class='metric-card' id='metricRam'>
                        <div class='metric-value'>--</div>
                        <div class='metric-label'>RAM</div>
                    </div>
                </div>
                <div class='chart-section'>
                    <div class='chart-card'>
                        <div class='chart-header'>
                            <span class='chart-title'>Frame History</span>
                            <div class='chart-meta'>
                                <span id='frameSelection' class='selection-badge' style='display:none;'>Pinned</span>
                                <button class='btn small' id='btnClearSelection' style='display:none;'>Clear</button>
                                <span>Last 300 frames</span>
                            </div>
                        </div>
                        <div class='chart-body'>
                            <svg id='frameChart'></svg>
                        </div>
                    </div>
                </div>
            </div>

            <!-- Flame Graph -->
            <div class='tab-view' id='tab-flame'>
                <div class='tab-toolbar'>
                    <div class='nav' style='padding: 2px;'>
                        <button class='nav-item active' id='btnFlameHierarchy'>Hierarchy</button>
                        <button class='nav-item' id='btnFlameScopes'>Scopes</button>
                    </div>
                    <button class='btn' id='btnFlameReset' style='margin-left: 12px;'>Reset Zoom</button>
                    <span style='color: var(--text-2); margin-left: 12px;'>Click to zoom in, right-click to zoom out</span>
                    <span id='flameScopeHint' style='color: var(--warning); margin-left: auto; display: none;'>Add ProfileScope to code to see function timing</span>
                </div>
                <div class='flame-container' id='flameContainer'>
                    <svg id='flameGraph'></svg>
                </div>
                <div id='flameTooltip' class='flame-tooltip'></div>
            </div>

            <!-- Hierarchy -->
            <div class='tab-view' id='tab-hierarchy'>
                <div class='tab-toolbar'>
                    <input type='text' class='search-input' placeholder='Search by name or type...' id='searchHierarchy'>
                    <button class='btn' id='btnExpandAll'>Expand All</button>
                    <button class='btn' id='btnCollapseAll'>Collapse All</button>
                </div>
                <div class='hierarchy-scroll' id='hierarchy'></div>
            </div>

            <!-- Hotspots -->
            <div class='tab-view' id='tab-hotspots'>
                <div class='tab-toolbar'>
                    <span style='color: var(--text-2);'>Showing top 10 slowest objects this frame</span>
                </div>
                <div class='hotspots-grid' id='hotspots'></div>
            </div>

            <!-- Scopes (aggregated ProfileScope stats) -->
            <div class='tab-view' id='tab-scopes'>
                <div class='tab-toolbar'>
                    <span style='color: var(--text-2);'>Aggregated ProfileScope statistics across frames</span>
                    <button class='btn' id='btnResetScopes' style='margin-left: auto;'>Reset Stats</button>
                </div>
                <div class='hierarchy-scroll' id='scopesTable' style='padding: 16px 20px;'>
                    <table style='width: 100%; border-collapse: collapse;'>
                        <thead>
                            <tr style='border-bottom: 1px solid var(--border);'>
                                <th style='text-align: left; padding: 10px 8px; color: var(--text-2); font-size: 11px; font-weight: 600;'>NAME</th>
                                <th style='text-align: right; padding: 10px 8px; color: var(--text-2); font-size: 11px; font-weight: 600;'>CALLS</th>
                                <th style='text-align: right; padding: 10px 8px; color: var(--text-2); font-size: 11px; font-weight: 600;'>AVG</th>
                                <th style='text-align: right; padding: 10px 8px; color: var(--text-2); font-size: 11px; font-weight: 600;'>MAX</th>
                                <th style='text-align: right; padding: 10px 8px; color: var(--text-2); font-size: 11px; font-weight: 600;'>P95</th>
                                <th style='text-align: right; padding: 10px 8px; color: var(--text-2); font-size: 11px; font-weight: 600;'>TOTAL</th>
                            </tr>
                        </thead>
                        <tbody id='scopesBody'></tbody>
                    </table>
                </div>
            </div>
        </main>

        <aside class='panel' id='panel'>
            <div class='panel-header'>
                <div class='panel-title'>Subsystems</div>
            </div>
            <div class='panel-scroll'>
                <div class='subsystem-card'>
                    <div class='subsystem-header'><div class='subsystem-icon'>T</div> Terrain</div>
                    <div class='subsystem-row'><span class='subsystem-label'>Draw Calls</span><span class='subsystem-value' id='terrainDraws'>-</span></div>
                    <div class='subsystem-row'><span class='subsystem-label'>Triangles</span><span class='subsystem-value' id='terrainTris'>-</span></div>
                    <div class='subsystem-row'><span class='subsystem-label'>Blocks</span><span class='subsystem-value' id='terrainBlocks'>-</span></div>
                </div>
                <div class='subsystem-card'>
                    <div class='subsystem-header'><div class='subsystem-icon'>O</div> Objects</div>
                    <div class='subsystem-row'><span class='subsystem-label'>Drawn</span><span class='subsystem-value' id='objDrawn'>-</span></div>
                    <div class='subsystem-row'><span class='subsystem-label'>Culled</span><span class='subsystem-value' id='objCulled'>-</span></div>
                    <div class='subsystem-row'><span class='subsystem-label'>Chunks</span><span class='subsystem-value' id='objChunks'>-</span></div>
                </div>
                <div class='subsystem-card'>
                    <div class='subsystem-header'><div class='subsystem-icon'>M</div> BMD Loader</div>
                    <div class='subsystem-row'><span class='subsystem-label'>VB Updates</span><span class='subsystem-value' id='bmdVB'>-</span></div>
                    <div class='subsystem-row'><span class='subsystem-label'>Cache Hit Rate</span><span class='subsystem-value' id='bmdCache'>-</span></div>
                </div>
                <div class='subsystem-card'>
                    <div class='subsystem-header'><div class='subsystem-icon'>P</div> Object Pool</div>
                    <div class='subsystem-row'><span class='subsystem-label'>Rents</span><span class='subsystem-value' id='poolRents'>-</span></div>
                    <div class='subsystem-row'><span class='subsystem-label'>Returns</span><span class='subsystem-value' id='poolReturns'>-</span></div>
                    <div class='subsystem-row'><span class='subsystem-label'>Leaks</span><span class='subsystem-value' id='poolLeaks'>-</span></div>
                </div>
                <div class='subsystem-card'>
                    <div class='subsystem-header'><div class='subsystem-icon'>R</div> Renderer</div>
                    <div class='subsystem-row'><span class='subsystem-label'>Draw Calls</span><span class='subsystem-value' id='renderDcTotal'>-</span></div>
                    <div class='subsystem-row' style='padding-left: 12px;'><span class='subsystem-label'>Terrain</span><span class='subsystem-value' id='renderDcTerrain'>-</span></div>
                    <div class='subsystem-row' style='padding-left: 12px;'><span class='subsystem-label'>Models</span><span class='subsystem-value' id='renderDcModels'>-</span></div>
                    <div class='subsystem-row' style='padding-left: 12px;'><span class='subsystem-label'>Effects</span><span class='subsystem-value' id='renderDcEffects'>-</span></div>
                    <div class='subsystem-row' style='padding-left: 12px;'><span class='subsystem-label'>UI</span><span class='subsystem-value' id='renderDcUI'>-</span></div>
                    <div class='subsystem-row'><span class='subsystem-label'>State Changes</span><span class='subsystem-value' id='renderStateChanges'>-</span></div>
                    <div class='subsystem-row'><span class='subsystem-label'>Batch Efficiency</span><span class='subsystem-value' id='renderBatchEff'>-</span></div>
                    <div class='subsystem-row'><span class='subsystem-label'>GPU Lighting</span><span class='subsystem-value' id='renderGpu'>-</span></div>
                </div>
                <div class='subsystem-card'>
                    <div class='subsystem-header'><div class='subsystem-icon'>M</div> Memory</div>
                    <div class='subsystem-row'><span class='subsystem-label'>Heap Size</span><span class='subsystem-value' id='memHeap'>-</span></div>
                    <div class='subsystem-row'><span class='subsystem-label'>Heap Delta</span><span class='subsystem-value' id='memDelta'>-</span></div>
                    <div class='subsystem-row'><span class='subsystem-label'>GC Gen0</span><span class='subsystem-value' id='memGen0'>-</span></div>
                    <div class='subsystem-row'><span class='subsystem-label'>GC Gen1</span><span class='subsystem-value' id='memGen1'>-</span></div>
                    <div class='subsystem-row'><span class='subsystem-label'>GC Gen2</span><span class='subsystem-value' id='memGen2'>-</span></div>
                    <div class='subsystem-row' id='memLeakRow' style='display: none;'><span class='subsystem-label' style='color: var(--danger);'>LEAK DETECTED</span><span class='subsystem-value' style='color: var(--danger);' id='memLeakFrames'>-</span></div>
                </div>
            </div>
        </aside>
    </div>

    <script>
        let ws = null;
        let paused = false;
        let recording = false;
        const frameHistory = [];
        const maxHistory = 300;
        const expandedNodes = new Set(); // Preserve expanded state across reloads
        let lastHotspots = []; // Keep last valid hotspots
        let latestFrameData = null;
        let latestHotspots = null;
        let selectedFrame = null;
        let selectedFrameData = null;
        let selectedFrameQuery = null;
        let selectedFrameChartIndex = null; // Index in frameHistory when pinned (tracked as frames shift)
        let selectedFrameHierarchy = null; // Hierarchy snapshot when frame was pinned
        let selectedFrameHotspots = null; // Hotspots snapshot when frame was pinned
        let flameScopePinnedFrame = null;
        let pausedFlameData = null; // Cached flame data when paused
        let pausedFlameScopeData = null;
        const hierarchyRefreshMs = 5000;
        const flameRefreshMs = 1000;

        function isTabActive(tabId) {
            return document.getElementById('tab-' + tabId)?.classList.contains('active');
        }

        function updateSelectionUi() {
            const badge = document.getElementById('frameSelection');
            const clearBtn = document.getElementById('btnClearSelection');
            if (selectedFrame === null || selectedFrame === undefined) {
                badge.style.display = 'none';
                badge.className = 'selection-badge';
                clearBtn.style.display = 'none';
                return;
            }

            // Use tracked chart index to determine if frame is still visible
            const isInHistory = selectedFrameChartIndex !== null && selectedFrameChartIndex >= 0 && selectedFrameChartIndex < frameHistory.length;
            badge.textContent = isInHistory ? `Pinned: #${selectedFrame}` : `Pinned: #${selectedFrame} (historic)`;
            badge.className = 'selection-badge ' + (isInHistory ? 'pinned-visible' : 'pinned-historic');
            badge.style.display = 'inline-flex';
            clearBtn.style.display = 'inline-flex';
        }

        async function selectFrame(frameIndex, offset, chartIndex) {
            if (frameIndex === null || frameIndex === undefined) return;
            await loadSelectedFrameData(frameIndex, offset, chartIndex);
            updateChart();
        }

        function clearSelectedFrame() {
            selectedFrame = null;
            selectedFrameData = null;
            selectedFrameQuery = null;
            selectedFrameChartIndex = null;
            selectedFrameHierarchy = null;
            selectedFrameHotspots = null;
            flameScopePinnedFrame = null;
            flameScopeData = null;
            updateSelectionUi();
            if (latestFrameData) updateMetricsFromFrame(latestFrameData);
            if (latestHotspots) updateHotspots(latestHotspots);
            loadFlameData(); // Reload current data
            updateChart();
        }

        async function loadSelectedFrameData(frameIndex, offset, chartIndex) {
            selectedFrame = null;
            selectedFrameData = null;
            selectedFrameQuery = null;
            selectedFrameChartIndex = chartIndex; // Track position in client's frameHistory
            selectedFrameHierarchy = null;
            selectedFrameHotspots = null;
            flameScopePinnedFrame = null;
            flameScopeData = null;
            updateSelectionUi();

            let query = '';
            if (offset !== null && offset !== undefined) {
                query = `?offset=${offset}`;
            } else {
                query = `?frame=${frameIndex}`;
            }

            try {
                let frameRes = await fetch('/api/frame' + query);
                if (!frameRes.ok && query.indexOf('offset=') >= 0) {
                    frameRes = await fetch(`/api/frame?frame=${frameIndex}`);
                }
                if (!frameRes.ok) {
                    clearSelectedFrame();
                    return;
                }
                selectedFrameData = await frameRes.json();
                selectedFrame = selectedFrameData.frame;
                selectedFrameQuery = query.indexOf('offset=') >= 0 ? query : `?frame=${selectedFrame}`;
                flameScopePinnedFrame = selectedFrame;
                updateSelectionUi();
                updateMetricsFromFrame(selectedFrameData);
            } catch (e) { console.error(e); }

            // Fetch and cache hotspots for pinned frame
            try {
                const hotspotsRes = await fetch('/api/hotspots' + (selectedFrameQuery ?? query));
                if (hotspotsRes.ok) {
                    const data = await hotspotsRes.json();
                    selectedFrameHotspots = data; // Cache for pinned frame
                    updateHotspots(data, false);
                }
            } catch (e) { console.error(e); }

            // Fetch and cache hierarchy snapshot for pinned frame
            try {
                const hierarchyRes = await fetch('/api/hierarchy');
                if (hierarchyRes.ok) {
                    selectedFrameHierarchy = await hierarchyRes.json();
                }
            } catch (e) { console.error(e); }

            try {
                const scopesRes = await fetch('/api/scopes' + (selectedFrameQuery ?? query));
                if (scopesRes.ok) {
                    flameScopeData = await scopesRes.json();
                } else {
                    flameScopeData = null;
                }
            } catch (e) { console.error(e); }

            // Reload flame graph with pinned data
            loadFlameData();
        }

        // WebSocket
        function connect() {
            ws = new WebSocket(`ws://${location.host}/ws`);
            ws.onopen = () => {
                document.getElementById('status').className = 'status-badge connected';
                document.getElementById('status').textContent = 'Connected';
                loadHierarchy();
            };
            ws.onclose = () => {
                document.getElementById('status').className = 'status-badge disconnected';
                document.getElementById('status').textContent = 'Disconnected';
                setTimeout(connect, 2000);
            };
            ws.onmessage = (e) => {
                if (paused) return;
                const msg = JSON.parse(e.data);
                if (msg.type === 'frame') handleLiveFrame(msg.data);
                if (msg.type === 'hotspots') {
                    latestHotspots = msg.data;
                    if (selectedFrame === null) updateHotspots(msg.data);
                }
            };
        }

        function handleLiveFrame(data) {
            latestFrameData = data;
            frameHistory.push({ frame: data.frame, total: data.totalMs, update: data.updateMs, draw: data.drawMs });
            if (frameHistory.length > maxHistory) {
                frameHistory.shift();
                // Track pinned frame position as array shifts left
                if (selectedFrameChartIndex !== null) {
                    selectedFrameChartIndex--;
                }
            }
            updateSelectionUi();
            updateChart();

            if (selectedFrame !== null) return;
            updateMetricsFromFrame(data);
        }

        function updateMetricsFromFrame(data) {
            setMetric('metricFps', data.fpsAvg?.toFixed(0) || '--', data.fpsAvg > 55 ? 'success' : data.fpsAvg > 30 ? 'warning' : 'danger');
            setMetric('metricFrame', (data.totalMs?.toFixed(2) || '--') + 'ms', data.totalMs < 12 ? 'success' : data.totalMs < 16.67 ? 'warning' : 'danger');
            setMetric('metricUpdate', (data.updateMs?.toFixed(2) || '--') + 'ms');
            setMetric('metricDraw', (data.drawMs?.toFixed(2) || '--') + 'ms');
            setMetric('metricBudget', (data.budgetPercent?.toFixed(0) || '--') + '%', data.budgetPercent < 80 ? 'success' : data.budgetPercent < 100 ? 'warning' : 'danger');

            if (data.process) {
                const cpu = data.process.cpuPercent ?? 0;
                setMetric('metricCpu', (typeof cpu === 'number' ? cpu.toFixed(0) : '--') + '%');

                if (data.process.gpuAvailable) {
                    const gpu = data.process.gpuPercent ?? 0;
                    setMetric('metricGpu', (typeof gpu === 'number' ? gpu.toFixed(0) : '--') + '%');
                } else {
                    setMetric('metricGpu', '--');
                }

                setMetric('metricRam', formatMemoryMb(data.process.ramMb));
            }

            const overhead = document.getElementById('overhead');
            if (data.profilerOverheadMs > 0.5) {
                overhead.style.display = 'inline';
                overhead.textContent = `Profiler: ${data.profilerOverheadMs.toFixed(2)}ms`;
            } else {
                overhead.style.display = 'none';
            }

            // Subsystems
            if (data.terrain) {
                document.getElementById('terrainDraws').textContent = data.terrain.drawCalls || 0;
                document.getElementById('terrainTris').textContent = (data.terrain.drawnTriangles || 0).toLocaleString();
                document.getElementById('terrainBlocks').textContent = data.terrain.drawnBlocks || 0;
            }
            if (data.objects) {
                document.getElementById('objDrawn').textContent = `${data.objects.drawnTotal || 0} / ${data.objects.totalObjects || 0}`;
                document.getElementById('objCulled').textContent = data.objects.culledByFrustum || 0;
                document.getElementById('objChunks').textContent = `${data.objects.staticChunksVisible || 0} / ${data.objects.staticChunksTotal || 0}`;
            }
            if (data.bmd) {
                document.getElementById('bmdVB').textContent = data.bmd.vbUpdates || 0;
                const total = (data.bmd.cacheHits || 0) + (data.bmd.cacheMisses || 0);
                document.getElementById('bmdCache').textContent = total > 0 ? `${((data.bmd.cacheHits / total) * 100).toFixed(0)}%` : '-';
            }
            if (data.pool) {
                document.getElementById('poolRents').textContent = data.pool.rents || 0;
                document.getElementById('poolReturns').textContent = data.pool.returns || 0;
                document.getElementById('poolLeaks').textContent = data.pool.leaks || 0;
            }
            if (data.render) {
                document.getElementById('renderGpu').textContent = data.render.objectGpuLighting ? 'ON' : 'OFF';
            }

            // Render stats breakdown
            if (data.renderStats) {
                const rs = data.renderStats;
                document.getElementById('renderDcTotal').textContent = rs.dcTotal || 0;
                document.getElementById('renderDcTerrain').textContent = rs.dcTerrain || 0;
                document.getElementById('renderDcModels').textContent = rs.dcModels || 0;
                document.getElementById('renderDcEffects').textContent = rs.dcEffects || 0;
                document.getElementById('renderDcUI').textContent = rs.dcUI || 0;
                const stateChanges = (rs.texSwitches || 0) + (rs.shaderSwitches || 0) + (rs.blendChanges || 0);
                document.getElementById('renderStateChanges').textContent = stateChanges;
                const batchEff = rs.batchEfficiency || 0;
                document.getElementById('renderBatchEff').textContent = (batchEff * 100).toFixed(0) + '%';
            }

            // Memory metrics
            if (data.memory) {
                const mem = data.memory;
                document.getElementById('memHeap').textContent = formatBytes(mem.heapBytes);
                const delta = mem.heapDelta || 0;
                const deltaStr = delta >= 0 ? '+' + formatBytes(delta) : '-' + formatBytes(-delta);
                const deltaEl = document.getElementById('memDelta');
                deltaEl.textContent = deltaStr;
                deltaEl.style.color = delta > 10240 ? 'var(--warning)' : delta > 102400 ? 'var(--danger)' : 'var(--text-0)';
                document.getElementById('memGen0').textContent = mem.gen0 || 0;
                document.getElementById('memGen1').textContent = mem.gen1 || 0;
                const gen2El = document.getElementById('memGen2');
                gen2El.textContent = mem.gen2 || 0;
                gen2El.style.color = mem.gen2Delta > 0 ? 'var(--danger)' : 'var(--text-0)';
                const leakRow = document.getElementById('memLeakRow');
                if (mem.isLeaking) {
                    leakRow.style.display = 'flex';
                    document.getElementById('memLeakFrames').textContent = mem.leakFrames + ' frames';
                } else {
                    leakRow.style.display = 'none';
                }
            }
        }

        function formatBytes(bytes) {
            if (typeof bytes !== 'number' || Number.isNaN(bytes)) return '--';
            if (bytes < 0) bytes = -bytes;
            if (bytes >= 1073741824) return (bytes / 1073741824).toFixed(2) + ' GB';
            if (bytes >= 1048576) return (bytes / 1048576).toFixed(1) + ' MB';
            if (bytes >= 1024) return (bytes / 1024).toFixed(1) + ' KB';
            return bytes + ' B';
        }

        function setMetric(id, value, state) {
            const el = document.getElementById(id);
            el.querySelector('.metric-value').textContent = value;
            el.className = 'metric-card' + (state ? ` ${state}` : '');
        }

        function formatMemoryMb(mb) {
            if (typeof mb !== 'number' || Number.isNaN(mb)) return '--';
            if (mb >= 1024) return (mb / 1024).toFixed(2) + ' GB';
            return mb.toFixed(0) + ' MB';
        }

        function updateHotspots(data, allowFallback = true) {
            // Keep last valid hotspots if new data is empty (due to object update skipping)
            if (allowFallback) {
                if (data.length > 0) {
                    lastHotspots = data;
                } else if (lastHotspots.length > 0) {
                    data = lastHotspots;
                }
            }

            document.getElementById('hotspotsCount').textContent = data.length;
            const container = document.getElementById('hotspots');
            if (data.length === 0) {
                container.innerHTML = ""<div style='padding: 40px; text-align: center; color: var(--text-2);'>No objects tracked this frame.<br>Objects skip updates when invisible or far away.</div>"";
                return;
            }
            const maxTime = Math.max(...data.map(h => h.totalMs), 1);

            container.innerHTML = data.map((h, i) => {
                const timeClass = h.totalMs > 0.5 ? 'slow' : h.totalMs > 0.1 ? 'medium' : 'fast';
                const updatePct = (h.updateMs / maxTime * 100).toFixed(0);
                const drawPct = (h.drawMs / maxTime * 100).toFixed(0);
                return `
                    <div class='hotspot-card' data-id='${h.id}'>
                        <div class='hotspot-header'>
                            <div class='hotspot-rank${i < 3 ? ' top' : ''}'>${i + 1}</div>
                            <div class='hotspot-title'>
                                <div class='hotspot-name'>${h.name}</div>
                                <div class='hotspot-type'>${h.type} @ (${h.posX?.toFixed(0) || 0}, ${h.posY?.toFixed(0) || 0})</div>
                            </div>
                            <div class='hotspot-time ${timeClass}'>${h.totalMs.toFixed(2)}ms</div>
                        </div>
                        <div class='hotspot-bars'>
                            <div class='hotspot-bar'>
                                <div class='hotspot-bar-label'>Update</div>
                                <div class='hotspot-bar-track'><div class='hotspot-bar-fill update' style='width:${updatePct}%'></div></div>
                                <div class='hotspot-bar-value'>${h.updateMs?.toFixed(2) || 0}ms</div>
                            </div>
                            <div class='hotspot-bar'>
                                <div class='hotspot-bar-label'>Draw</div>
                                <div class='hotspot-bar-track'><div class='hotspot-bar-fill draw' style='width:${drawPct}%'></div></div>
                                <div class='hotspot-bar-value'>${h.drawMs?.toFixed(2) || 0}ms</div>
                            </div>
                        </div>
                    </div>
                `;
            }).join('');
        }

        function updateChart() {
            const svg = d3.select('#frameChart');
            const rect = svg.node()?.getBoundingClientRect();
            if (!rect) return;
            const width = rect.width || 600;
            const height = rect.height || 200;
            svg.selectAll('*').remove();
            if (frameHistory.length < 2) return;

            const margin = { top: 10, right: 10, bottom: 30, left: 45 };
            const w = width - margin.left - margin.right;
            const h = height - margin.top - margin.bottom;

            const x = d3.scaleLinear().domain([0, frameHistory.length - 1]).range([0, w]);
            const y = d3.scaleLinear().domain([0, Math.max(20, d3.max(frameHistory, d => d.total) * 1.1)]).range([h, 0]);

            const g = svg.append('g').attr('transform', `translate(${margin.left},${margin.top})`);

            // Budget line
            g.append('line').attr('x1', 0).attr('x2', w).attr('y1', y(16.67)).attr('y2', y(16.67))
                .attr('stroke', '#ef4444').attr('stroke-dasharray', '4').attr('opacity', 0.5);
            g.append('text').attr('x', w - 5).attr('y', y(16.67) - 5).attr('fill', '#ef4444')
                .attr('font-size', '10px').attr('text-anchor', 'end').text('16.67ms budget');

            // Area
            const area = d3.area().x((d, i) => x(i)).y0(h).y1(d => y(d.total)).curve(d3.curveMonotoneX);
            g.append('path').datum(frameHistory).attr('fill', 'rgba(59, 130, 246, 0.1)').attr('d', area);

            // Lines
            const line = d3.line().x((d, i) => x(i)).y(d => y(d.total)).curve(d3.curveMonotoneX);
            g.append('path').datum(frameHistory).attr('fill', 'none').attr('stroke', '#3b82f6').attr('stroke-width', 2).attr('d', line);

            const updateLine = d3.line().x((d, i) => x(i)).y(d => y(d.update)).curve(d3.curveMonotoneX);
            g.append('path').datum(frameHistory).attr('fill', 'none').attr('stroke', '#22c55e').attr('stroke-width', 1.5).attr('d', updateLine);

            // Axes
            g.append('g').attr('transform', `translate(0,${h})`).call(d3.axisBottom(x).ticks(6).tickFormat(d => '')).attr('color', '#3f3f46');
            g.append('g').call(d3.axisLeft(y).ticks(5).tickFormat(d => d + 'ms')).attr('color', '#3f3f46');

            if (selectedFrame !== null && selectedFrameChartIndex !== null) {
                // Use tracked chart index for positioning (not frame number lookup)
                const isVisible = selectedFrameChartIndex >= 0 && selectedFrameChartIndex < frameHistory.length;
                if (isVisible) {
                    // Frame is visible in history - draw solid marker at tracked position
                    const xPos = x(selectedFrameChartIndex);
                    g.append('line')
                        .attr('x1', xPos).attr('x2', xPos)
                        .attr('y1', 0).attr('y2', h)
                        .attr('stroke', '#f59e0b')
                        .attr('stroke-width', 2)
                        .attr('opacity', 0.8);
                    g.append('circle')
                        .attr('cx', xPos)
                        .attr('cy', y(frameHistory[selectedFrameChartIndex]?.total || 0))
                        .attr('r', 4)
                        .attr('fill', '#f59e0b');
                } else {
                    // Frame scrolled out of visible history - show indicator at left edge
                    g.append('line')
                        .attr('x1', 0).attr('x2', 0)
                        .attr('y1', 0).attr('y2', h)
                        .attr('stroke', '#f59e0b')
                        .attr('stroke-width', 2)
                        .attr('stroke-dasharray', '4,2')
                        .attr('opacity', 0.6);
                    g.append('polygon')
                        .attr('points', `0,${h/2-8} 8,${h/2} 0,${h/2+8}`)
                        .attr('fill', '#f59e0b')
                        .attr('opacity', 0.6);
                    g.append('text')
                        .attr('x', 12)
                        .attr('y', h/2 + 4)
                        .attr('fill', '#f59e0b')
                        .attr('font-size', '10px')
                        .attr('opacity', 0.8)
                        .text(`#${selectedFrame} (scrolled out)`);
                }
            }

            g.append('rect')
                .attr('x', 0).attr('y', 0)
                .attr('width', w).attr('height', h)
                .attr('fill', 'transparent')
                .style('cursor', 'crosshair')
                .on('click', (event) => {
                    if (frameHistory.length === 0) return;
                    const [mx] = d3.pointer(event, g.node());
                    const idx = Math.round(x.invert(mx));
                    const clamped = Math.max(0, Math.min(frameHistory.length - 1, idx));
                    const frameId = frameHistory[clamped]?.frame;
                    const offset = (frameHistory.length - 1) - clamped;
                    // Pass clamped as chartIndex - this is the position in client's frameHistory
                    if (frameId !== undefined) selectFrame(frameId, offset, clamped);
                })
                .on('contextmenu', (event) => {
                    event.preventDefault();
                    clearSelectedFrame();
                });
        }

        let isHierarchyLoading = false;
        async function loadHierarchy() {
            if (isHierarchyLoading) return;
            isHierarchyLoading = true;
            try {
                let data;
                // When frame is pinned, use cached hierarchy from that moment
                if (selectedFrame !== null && selectedFrameHierarchy) {
                    data = selectedFrameHierarchy;
                } else if (paused) {
                    // When paused, use same cache as Flame Graph for consistency
                    if (pausedFlameData) {
                        data = pausedFlameData;
                    } else {
                        // No cache yet - fetch once and cache
                        const res = await fetch('/api/hierarchy');
                        data = await res.json();
                        pausedFlameData = data;
                    }
                } else {
                    const res = await fetch('/api/hierarchy');
                    data = await res.json();
                    // Update pause cache with latest data
                    pausedFlameData = data;
                }
                const container = document.getElementById('hierarchy');
                container.innerHTML = '';
                renderNode(data, container);
                document.getElementById('hierarchyCount').textContent = countNodes(data);
            } catch (e) { console.error(e); }
            finally { isHierarchyLoading = false; }
        }

        function countNodes(node) {
            let c = 1;
            if (node.children) node.children.forEach(ch => c += countNodes(ch));
            return c;
        }

        function renderNode(node, container) {
            const nodeId = node.id || node.name;
            const isExpanded = expandedNodes.has(nodeId);
            const div = document.createElement('div');
            div.className = 'tree-node' + (isExpanded ? '' : ' collapsed');
            div.dataset.nodeId = nodeId;
            const hasChildren = node.children?.length > 0;
            const totalMs = (node.updateMs || 0) + (node.drawMs || 0);
            const selfMs = (node.selfUpdateMs || 0) + (node.selfDrawMs || 0);
            const timeClass = totalMs > 0.5 ? 'slow' : totalMs > 0.1 ? 'medium' : 'fast';
            const selfInfo = hasChildren && selfMs > 0 ? ` (self: ${selfMs.toFixed(2)}ms)` : '';

            div.innerHTML = `
                <div class='tree-row'>
                    <div class='tree-toggle ${hasChildren ? '' : 'empty'}'>${isExpanded ? '▼' : '▶'}</div>
                    <div class='tree-info'>
                        <div class='tree-name'>${node.name}</div>
                        <div class='tree-meta'>${node.type || ''}${node.childCount ? ' • ' + node.childCount + ' children' : ''}${selfInfo}</div>
                    </div>
                    <div class='tree-timing ${timeClass}'>${totalMs.toFixed(2)}ms</div>
                </div>
            `;

            if (hasChildren) {
                const childContainer = document.createElement('div');
                childContainer.className = 'tree-children';
                node.children.forEach(ch => renderNode(ch, childContainer));
                div.appendChild(childContainer);
                div.querySelector('.tree-toggle').onclick = (e) => {
                    e.stopPropagation();
                    const isNowCollapsed = div.classList.toggle('collapsed');
                    e.target.textContent = isNowCollapsed ? '▶' : '▼';
                    if (isNowCollapsed) {
                        expandedNodes.delete(nodeId);
                    } else {
                        expandedNodes.add(nodeId);
                    }
                };
            }
            container.appendChild(div);
        }

        // Flame Graph
        let flameData = null;
        let flameScopeData = null;
        let flameMode = 'hierarchy'; // 'hierarchy' or 'scopes'
        let flameZoomStack = [];
        let flameZoomPath = [];
        const flameColors = ['#3b82f6', '#8b5cf6', '#ec4899', '#f59e0b', '#10b981', '#06b6d4', '#6366f1'];
        const scopeColors = ['#22c55e', '#10b981', '#14b8a6', '#06b6d4', '#0ea5e9', '#3b82f6', '#6366f1'];

        function getFlameNodeKey(node, isScopes) {
            if (!node) return '';
            if (isScopes) {
                return (node.name || '') + '|' + (node.category || '');
            }
            return node.id || node.name || '';
        }

        function rebuildFlameZoomStack(rootNode, isScopes) {
            if (!rootNode || flameZoomPath.length === 0) return [];
            let current = rootNode;
            const newStack = [];
            for (const key of flameZoomPath) {
                if (!current?.children || current.children.length === 0) break;
                const next = current.children.find(child => getFlameNodeKey(child, isScopes) === key);
                if (!next) break;
                newStack.push(next);
                current = next;
            }
            flameZoomPath = newStack.map(node => getFlameNodeKey(node, isScopes));
            return newStack;
        }

        let isFlameLoading = false;
        async function loadFlameData() {
            if (isFlameLoading) return;
            isFlameLoading = true;
            try {
                if (flameMode === 'hierarchy') {
                    // When frame is pinned, use cached hierarchy from that moment
                    if (selectedFrame !== null && selectedFrameHierarchy) {
                        flameData = selectedFrameHierarchy;
                        flameZoomStack = rebuildFlameZoomStack(flameData, false);
                        renderFlameGraph(flameData, false);
                        return;
                    }

                    // When paused, use cached data or show existing
                    if (paused) {
                        if (pausedFlameData) {
                            flameData = pausedFlameData;
                            flameZoomStack = rebuildFlameZoomStack(flameData, false);
                            renderFlameGraph(flameData, false);
                        } else if (flameData) {
                            // No cache but have current data - just re-render it
                            renderFlameGraph(flameData, false);
                        }
                        // If no data at all, leave as-is (paused before any data loaded)
                        return;
                    }

                    const res = await fetch('/api/hierarchy');
                    flameData = await res.json();
                    // Cache for pause mode
                    pausedFlameData = flameData;
                    flameZoomStack = rebuildFlameZoomStack(flameData, false);
                    renderFlameGraph(flameData, false);
                } else {
                    if (selectedFrame !== null && flameScopePinnedFrame === selectedFrame) {
                        const hint = document.getElementById('flameScopeHint');
                        if (!flameScopeData) {
                            hint.style.display = 'inline';
                            renderFlameGraph(null, true);
                        } else {
                            hint.style.display = 'none';
                            flameZoomStack = rebuildFlameZoomStack(flameScopeData, true);
                            renderFlameGraph(flameScopeData, true);
                        }
                        return;
                    }

                    // When paused, use cached scope data or show existing
                    if (paused) {
                        if (pausedFlameScopeData) {
                            flameScopeData = pausedFlameScopeData;
                            const hint = document.getElementById('flameScopeHint');
                            hint.style.display = 'none';
                            flameZoomStack = rebuildFlameZoomStack(flameScopeData, true);
                            renderFlameGraph(flameScopeData, true);
                        } else if (flameScopeData) {
                            renderFlameGraph(flameScopeData, true);
                        }
                        return;
                    }

                    const frameQuery = selectedFrameQuery ?? (selectedFrame !== null ? `?frame=${selectedFrame}` : '');
                    const res = await fetch('/api/scopes' + frameQuery);
                    if (!res.ok) {
                        const hint = document.getElementById('flameScopeHint');
                        hint.style.display = 'inline';
                        renderFlameGraph(null, true);
                        return;
                    }
                    flameScopeData = await res.json();
                    // Cache for pause mode
                    pausedFlameScopeData = flameScopeData;
                    const hint = document.getElementById('flameScopeHint');
                    if (!flameScopeData) {
                        hint.style.display = 'inline';
                        renderFlameGraph(null, true);
                    } else {
                        hint.style.display = 'none';
                        flameZoomStack = rebuildFlameZoomStack(flameScopeData, true);
                        renderFlameGraph(flameScopeData, true);
                    }
                }
            } catch (e) { console.error(e); }
            finally { isFlameLoading = false; }
        }

        document.getElementById('btnFlameHierarchy').onclick = () => {
            flameMode = 'hierarchy';
            flameZoomStack = [];
            flameZoomPath = [];
            document.getElementById('btnFlameHierarchy').classList.add('active');
            document.getElementById('btnFlameScopes').classList.remove('active');
            document.getElementById('flameScopeHint').style.display = 'none';
            loadFlameData();
        };

        document.getElementById('btnFlameScopes').onclick = () => {
            flameMode = 'scopes';
            flameZoomStack = [];
            flameZoomPath = [];
            document.getElementById('btnFlameScopes').classList.add('active');
            document.getElementById('btnFlameHierarchy').classList.remove('active');
            loadFlameData();
        };

        function renderFlameGraph(rootNode, isScopes = false) {
            const svg = d3.select('#flameGraph');
            const container = document.getElementById('flameContainer');
            if (!container) return;
            const rect = container.getBoundingClientRect();
            const width = rect.width - 32;
            const height = rect.height - 32;
            svg.selectAll('*').remove();

            if (!rootNode) {
                svg.append('text')
                    .attr('x', width / 2)
                    .attr('y', 50)
                    .attr('text-anchor', 'middle')
                    .attr('fill', 'var(--text-2)')
                    .text(isScopes ? 'No scopes recorded. Add ProfileScope to code.' : 'Loading...');
                return;
            }

            const rowHeight = 28;
            const padding = 2;
            const colors = isScopes ? scopeColors : flameColors;

            // Get total time for a node (works for both hierarchy and scope data)
            function getNodeTime(n) {
                return isScopes ? (n.durationMs || 0) : ((n.updateMs || 0) + (n.drawMs || 0));
            }

            // Flatten hierarchy with depth info
            const nodes = [];
            function flatten(node, depth, startX, parentWidth) {
                const totalMs = getNodeTime(node);
                if (totalMs <= 0) return;

                const zoomRootTime = getNodeTime(flameZoomStack.length > 0 ? flameZoomStack[0] : rootNode);
                const nodeWidth = parentWidth * (totalMs / (zoomRootTime || 1));

                nodes.push({ node, depth, x: startX, width: nodeWidth, totalMs });

                if (node.children) {
                    let childX = startX;
                    node.children.forEach(child => {
                        const childTotal = getNodeTime(child);
                        if (childTotal > 0) {
                            const childWidth = nodeWidth * (childTotal / totalMs);
                            flatten(child, depth + 1, childX, nodeWidth);
                            childX += childWidth;
                        }
                    });
                }
            }

            const zoomRoot = flameZoomStack.length > 0 ? flameZoomStack[flameZoomStack.length - 1] : rootNode;
            flatten(zoomRoot, 0, 0, width);

            const maxDepth = Math.max(...nodes.map(n => n.depth), 0) + 1;
            const usedHeight = Math.min(height, maxDepth * rowHeight);

            svg.attr('viewBox', `0 0 ${width} ${usedHeight}`);

            const tooltip = document.getElementById('flameTooltip');

            nodes.forEach(({ node, depth, x, width: nodeWidth, totalMs }) => {
                if (nodeWidth < 1) return;

                const y = depth * rowHeight;
                const color = colors[depth % colors.length];

                const g = svg.append('g');

                g.append('rect')
                    .attr('class', 'flame-rect')
                    .attr('x', x + padding/2)
                    .attr('y', y + padding/2)
                    .attr('width', Math.max(0, nodeWidth - padding))
                    .attr('height', rowHeight - padding)
                    .attr('rx', 3)
                    .attr('fill', color)
                    .on('click', () => {
                        if (node.children?.length > 0) {
                            flameZoomStack.push(node);
                            flameZoomPath.push(getFlameNodeKey(node, isScopes));
                            renderFlameGraph(rootNode, isScopes);
                        }
                    })
                    .on('contextmenu', (e) => {
                        e.preventDefault();
                        if (flameZoomStack.length > 0) {
                            flameZoomStack.pop();
                            flameZoomPath.pop();
                            renderFlameGraph(rootNode, isScopes);
                        }
                    })
                    .on('mousemove', (e) => {
                        let selfMs, tooltipContent;
                        if (isScopes) {
                            selfMs = node.selfMs || 0;
                            tooltipContent = `
                                <div class='flame-tooltip-name'>${node.name}</div>
                                <div class='flame-tooltip-type'>${node.category || 'scope'}</div>
                                <div class='flame-tooltip-row'><span class='flame-tooltip-label'>Duration</span><span class='flame-tooltip-value'>${totalMs.toFixed(3)}ms</span></div>
                                <div class='flame-tooltip-row'><span class='flame-tooltip-label'>Self</span><span class='flame-tooltip-value'>${selfMs.toFixed(3)}ms</span></div>
                                <div class='flame-tooltip-row'><span class='flame-tooltip-label'>Children</span><span class='flame-tooltip-value'>${(totalMs - selfMs).toFixed(3)}ms</span></div>
                            `;
                        } else {
                            selfMs = (node.selfUpdateMs || 0) + (node.selfDrawMs || 0);
                            tooltipContent = `
                                <div class='flame-tooltip-name'>${node.name}</div>
                                <div class='flame-tooltip-type'>${node.type || ''}</div>
                                <div class='flame-tooltip-row'><span class='flame-tooltip-label'>Total</span><span class='flame-tooltip-value'>${totalMs.toFixed(2)}ms</span></div>
                                <div class='flame-tooltip-row'><span class='flame-tooltip-label'>Self</span><span class='flame-tooltip-value'>${selfMs.toFixed(2)}ms</span></div>
                                <div class='flame-tooltip-row'><span class='flame-tooltip-label'>Update</span><span class='flame-tooltip-value'>${(node.updateMs || 0).toFixed(2)}ms</span></div>
                                <div class='flame-tooltip-row'><span class='flame-tooltip-label'>Draw</span><span class='flame-tooltip-value'>${(node.drawMs || 0).toFixed(2)}ms</span></div>
                            `;
                        }
                        tooltip.innerHTML = tooltipContent;
                        tooltip.style.left = (e.clientX + 12) + 'px';
                        tooltip.style.top = (e.clientY + 12) + 'px';
                        tooltip.classList.add('visible');
                    })
                    .on('mouseout', () => {
                        tooltip.classList.remove('visible');
                    });

                if (nodeWidth > 40) {
                    g.append('text')
                        .attr('class', 'flame-text')
                        .attr('x', x + nodeWidth / 2)
                        .attr('y', y + rowHeight / 2 + 4)
                        .attr('text-anchor', 'middle')
                        .text(nodeWidth > 80 ? `${node.name} (${totalMs.toFixed(2)}ms)` : node.name);
                }
            });
        }

        document.getElementById('btnFlameReset').onclick = () => {
            flameZoomStack = [];
            flameZoomPath = [];
            loadFlameData();
        };

        // Scope Stats
        let isScopesLoading = false;
        async function loadScopeStats() {
            if (isScopesLoading) return;
            isScopesLoading = true;
            try {
                const res = await fetch('/api/scopestats');
                const data = await res.json();
                document.getElementById('scopesCount').textContent = data.length;
                const tbody = document.getElementById('scopesBody');
                if (data.length === 0) {
                    tbody.innerHTML = `<tr><td colspan='6' style='padding: 40px; text-align: center; color: var(--text-2);'>No ProfileScope data recorded yet.<br>Add ProfileScope.Begin() to your code.</td></tr>`;
                    return;
                }
                tbody.innerHTML = data.map(s => {
                    const avgClass = s.avgMs > 1 ? 'color: var(--danger)' : s.avgMs > 0.5 ? 'color: var(--warning)' : '';
                    const maxClass = s.maxMs > 2 ? 'color: var(--danger)' : s.maxMs > 1 ? 'color: var(--warning)' : '';
                    return `<tr style='border-bottom: 1px solid var(--border);'>
                        <td style='padding: 10px 8px;'>
                            <div style='font-weight: 500;'>${s.name}</div>
                            ${s.category ? `<div style='font-size: 11px; color: var(--text-2);'>${s.category}</div>` : ''}
                        </td>
                        <td style='padding: 10px 8px; text-align: right; font-variant-numeric: tabular-nums;'>${s.calls.toLocaleString()}</td>
                        <td style='padding: 10px 8px; text-align: right; font-variant-numeric: tabular-nums; ${avgClass}'>${s.avgMs.toFixed(3)}ms</td>
                        <td style='padding: 10px 8px; text-align: right; font-variant-numeric: tabular-nums; ${maxClass}'>${s.maxMs.toFixed(3)}ms</td>
                        <td style='padding: 10px 8px; text-align: right; font-variant-numeric: tabular-nums;'>${s.p95Ms.toFixed(3)}ms</td>
                        <td style='padding: 10px 8px; text-align: right; font-variant-numeric: tabular-nums;'>${s.totalMs.toFixed(1)}ms</td>
                    </tr>`;
                }).join('');
            } catch (e) { console.error(e); }
            finally { isScopesLoading = false; }
        }

        document.getElementById('btnResetScopes').onclick = async () => {
            await fetch('/api/scopestats/reset', { method: 'POST' });
            loadScopeStats();
        };

        // Tab navigation (only nav items with data-tab attribute)
        document.querySelectorAll('.nav-item[data-tab]').forEach(tab => {
            tab.onclick = () => {
                document.querySelectorAll('.nav-item[data-tab]').forEach(t => t.classList.remove('active'));
                document.querySelectorAll('.tab-view').forEach(v => v.classList.remove('active'));
                tab.classList.add('active');
                document.getElementById('tab-' + tab.dataset.tab).classList.add('active');
                if (tab.dataset.tab === 'dashboard') setTimeout(updateChart, 50);
                // Hierarchy: always call loadHierarchy - it will use cache when paused/pinned
                if (tab.dataset.tab === 'hierarchy') setTimeout(loadHierarchy, 50);
                // Flame: always call loadFlameData - it will use cache when paused/pinned
                if (tab.dataset.tab === 'flame') setTimeout(loadFlameData, 50);
                // Scopes: load aggregated stats
                if (tab.dataset.tab === 'scopes') setTimeout(loadScopeStats, 50);
            };
        });

        // Controls
        document.getElementById('btnPause').onclick = () => {
            paused = !paused;
            document.getElementById('btnPause').classList.toggle('active', paused);
            document.getElementById('btnPause').innerHTML = paused ? '<span>▶</span> Resume' : '<span>❚❚</span> Pause';
        };

        document.getElementById('btnRecord').onclick = async () => {
            if (!recording) {
                await fetch('/api/recording/start', { method: 'POST' });
                recording = true;
                document.getElementById('btnRecord').classList.add('recording');
                document.getElementById('btnRecord').innerHTML = '<span>■</span> Stop';
            } else {
                const res = await fetch('/api/recording/stop', { method: 'POST' });
                recording = false;
                document.getElementById('btnRecord').classList.remove('recording');
                document.getElementById('btnRecord').innerHTML = '<span>●</span> Record';

                if (res.ok) {
                    const data = await res.json();
                    if (data && !data.error) {
                        const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
                        const a = document.createElement('a');
                        a.href = URL.createObjectURL(blob);
                        a.download = `recording-${new Date().toISOString().slice(0,19).replace(/:/g,'-')}.json`;
                        a.click();
                    }
                }
            }
        };

        document.getElementById('btnExport').onclick = async () => {
            const res = await fetch('/api/history');
            const data = await res.json();
            const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
            const a = document.createElement('a');
            a.href = URL.createObjectURL(blob);
            a.download = `profiler-${new Date().toISOString().slice(0,19).replace(/:/g,'-')}.json`;
            a.click();
        };

        document.getElementById('btnClearSelection').onclick = () => {
            clearSelectedFrame();
        };

        // Search
        document.getElementById('searchHierarchy').oninput = (e) => {
            const q = e.target.value.toLowerCase();
            document.querySelectorAll('#hierarchy .tree-node').forEach(n => {
                const name = n.querySelector('.tree-name')?.textContent.toLowerCase() || '';
                const type = n.querySelector('.tree-meta')?.textContent.toLowerCase() || '';
                n.style.display = (name.includes(q) || type.includes(q)) ? '' : 'none';
            });
        };

        document.getElementById('btnExpandAll').onclick = () => {
            document.querySelectorAll('#hierarchy .tree-node').forEach(n => {
                n.classList.remove('collapsed');
                const t = n.querySelector('.tree-toggle');
                if (t && !t.classList.contains('empty')) {
                    t.textContent = '▼';
                    if (n.dataset.nodeId) expandedNodes.add(n.dataset.nodeId);
                }
            });
        };

        document.getElementById('btnCollapseAll').onclick = () => {
            document.querySelectorAll('#hierarchy .tree-node').forEach(n => {
                n.classList.add('collapsed');
                const t = n.querySelector('.tree-toggle');
                if (t && !t.classList.contains('empty')) {
                    t.textContent = '▶';
                    if (n.dataset.nodeId) expandedNodes.delete(n.dataset.nodeId);
                }
            });
        };

        window.addEventListener('resize', updateChart);
        connect();
        setInterval(() => {
            if (paused) return;
            // Don't auto-refresh when frame is pinned (data is frozen)
            if (selectedFrame !== null) return;
            if (isTabActive('hierarchy')) loadHierarchy();
        }, hierarchyRefreshMs);
        setInterval(() => {
            if (paused) return;
            // Don't auto-refresh when frame is pinned (data is frozen)
            if (selectedFrame !== null) return;
            if (isTabActive('flame')) {
                loadFlameData();
            }
        }, flameRefreshMs);
        setInterval(() => {
            // Scopes aggregates across session, refresh even when paused
            if (isTabActive('scopes')) loadScopeStats();
        }, 2000); // Every 2 seconds
    </script>
</body>
</html>";
    }
}
#endif
