(() => {
    "use strict";

    const map = L.map("visualMap").setView([4.6, -74.1], 6);

    L.tileLayer(
        "https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png",
        {
            attribution: "&copy; OpenStreetMap contributors",
            maxZoom: 18
        }
    ).addTo(map);

    let surface = L.layerGroup().addTo(map);
    let currentData = null;

    const form = document.getElementById("visualForm");
    const status = document.getElementById("visualStatus");
    const legend = document.getElementById("visualLegend");
    const comparison = document.getElementById("visualComparison");
    const eventSelect = document.getElementById("eventId");

    function parameters() {
        const query = new URLSearchParams({
            eventId: eventSelect.value,
            method: document.getElementById("method").value,
            metric: document.getElementById("metric").value,
            radiusKm: document.getElementById("radiusKm").value,
            cellKm: "1",
            maxCells: "1200"
        });

        const from = document.getElementById("dateFrom").value;
        const to = document.getElementById("dateTo").value;

        if (from) query.set("from", `${from}T00:00:00Z`);
        if (to) query.set("to", `${to}T23:59:59Z`);

        return query;
    }

    async function requestJson(url) {
        const response = await fetch(url);
        const payload = await response.json();

        if (!response.ok) {
            throw new Error(
                payload.detail || payload.message ||
                payload.title || `HTTP ${response.status}`
            );
        }

        return payload;
    }

    function colorFor(value, levels) {
        for (let i = levels.length - 1; i >= 0; i--) {
            if (value >= levels[i].from) {
                return levels[i].color;
            }
        }
        return levels[0]?.color ?? "#888888";
    }

    function drawSurface(data) {
        currentData = data;
        surface.clearLayers();
        legend.replaceChildren();

        const grid = data.grid;
        const bounds = [
            [grid.minLat, grid.minLon],
            [
                grid.minLat + grid.rows * grid.stepLat,
                grid.minLon + grid.cols * grid.stepLon
            ]
        ];

        let visible = 0;

        for (let row = 0; row < grid.rows; row++) {
            for (let col = 0; col < grid.cols; col++) {
                const value = grid.values[row][col];

                if (value === null || !Number.isFinite(value)) {
                    continue;
                }

                const lat = grid.minLat + row * grid.stepLat;
                const lon = grid.minLon + col * grid.stepLon;

                L.rectangle(
                    [
                        [lat, lon],
                        [lat + grid.stepLat, lon + grid.stepLon]
                    ],
                    {
                        stroke: false,
                        fillColor: colorFor(value, data.levels),
                        fillOpacity: 0.55,
                        fill: true,
                        interactive: false
                    }
                ).addTo(surface);

                visible++;
            }
        }

        map.fitBounds(bounds);

        for (const level of data.levels) {
            const item = document.createElement("span");
            const swatch = document.createElement("i");
            swatch.className = "visual-swatch";
            swatch.style.backgroundColor = level.color;

            item.appendChild(swatch);
            item.appendChild(document.createTextNode(level.level));
            legend.appendChild(item);
        }

        status.textContent =
            `${data.method}: ${data.sampleCount} muestras, ` +
            `${visible} celdas estimadas de ${grid.rows * grid.cols}.`;
    }

    async function loadEvents() {
        try {
            const events = await requestJson("/visualizacion/events");

            eventSelect.replaceChildren();

            for (const event of events) {
                const option = document.createElement("option");
                option.value = event.id;
                option.textContent = event.name;
                eventSelect.appendChild(option);
            }

            if (events.length === 0) {
                status.textContent = "No hay eventos disponibles.";
                return;
            }

            await generate();
        } catch (error) {
            status.textContent = `Error cargando eventos: ${error.message}`;
        }
    }

    async function generate() {
        if (!eventSelect.value) return;

        status.textContent = "Calculando superficie...";

        try {
            const data = await requestJson(
                `/visualizacion/data?${parameters()}`
            );

            drawSurface(data);
        } catch (error) {
            surface.clearLayers();
            legend.replaceChildren();
            status.textContent = `No se pudo generar: ${error.message}`;
        }
    }

    async function compare() {
        if (!eventSelect.value) return;

        comparison.textContent = "Calculando validación cruzada...";

        try {
            const result = await requestJson(
                `/visualizacion/compare?${parameters()}`
            );

            const table = document.createElement("table");
            table.className = "table table-sm table-striped small";

            const header = document.createElement("thead");
            header.innerHTML =
                "<tr><th>Método</th><th>MAE</th><th>RMSE</th><th>Cobertura</th></tr>";
            table.appendChild(header);

            const body = document.createElement("tbody");

            for (const method of result.methods) {
                const row = document.createElement("tr");

                const cells = [
                    method.method,
                    method.mae === null ? "—" : method.mae.toFixed(2),
                    method.rmse === null ? "—" : method.rmse.toFixed(2),
                    `${method.evaluated}/${method.total}`
                ];

                for (const value of cells) {
                    const td = document.createElement("td");
                    td.textContent = value;
                    row.appendChild(td);
                }

                body.appendChild(row);
            }

            table.appendChild(body);
            comparison.replaceChildren(table);
        } catch (error) {
            comparison.textContent = `Error: ${error.message}`;
        }
    }

    form.addEventListener("submit", event => {
        event.preventDefault();
        generate();
    });

    document.getElementById("compareBtn")
        .addEventListener("click", compare);

    function exportGeoJson() {
        if (!currentData) {
            status.textContent =
                "Primero debes generar una superficie.";
            return;
        }

        const grid = currentData.grid;
        const features = [];

        for (let row = 0; row < grid.rows; row++) {
            for (let col = 0; col < grid.cols; col++) {
                const value = grid.values[row][col];

                if (value === null || !Number.isFinite(value))
                    continue;

                const south = grid.minLat + row * grid.stepLat;
                const north = south + grid.stepLat;
                const west = grid.minLon + col * grid.stepLon;
                const east = west + grid.stepLon;

                features.push({
                    type: "Feature",
                    geometry: {
                        type: "Polygon",
                        coordinates: [[
                            [west, south],
                            [east, south],
                            [east, north],
                            [west, north],
                            [west, south]
                        ]]
                    },
                    properties: {
                        method: currentData.method,
                        metric: currentData.metric,
                        value,
                        eventId: currentData.eventId,
                        sampleCount: currentData.sampleCount
                    }
                });
            }
        }

        const collection = {
            type: "FeatureCollection",
            features
        };

        const blob = new Blob(
            [JSON.stringify(collection)],
            { type: "application/geo+json" }
        );

        const url = URL.createObjectURL(blob);
        const link = document.createElement("a");

        link.href = url;
        link.download =
            `disastermaps-${currentData.method.toLowerCase()}.geojson`;

        document.body.appendChild(link);
        link.click();
        link.remove();

        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }

    document.getElementById("exportGeoJsonBtn")
        .addEventListener("click", exportGeoJson);
    loadEvents();
})();