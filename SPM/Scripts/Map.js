// Map-TomTom.js
(function (window, document) {
    const apiKey = 'J3L0PYtBvPci4B36q2aVL4712eohQbPD';

    // 1) Lazy-load MapLibre’s CSS & JS from unpkg
    const css = document.createElement('link');
    css.rel = 'stylesheet';
    css.href = 'https://unpkg.com/maplibre-gl@2.4.0/dist/maplibre-gl.css';
    document.head.appendChild(css);

    const script = document.createElement('script');
    script.src = 'https://unpkg.com/maplibre-gl@2.4.0/dist/maplibre-gl.js';
    script.async = true;
    script.onload = initMap;
    document.head.appendChild(script);

    // 2) Kick off once MapLibre is ready
    function initMap() {
        const styleUrl =
            `https://api.tomtom.com/style/1/style/22.*/?` +
            `key=${apiKey}&map=2/basic_street-light`;

        const pathSegments = window.location.pathname.split('/');
        // The first element after the domain is typically at index 1 (index 0 is an empty string for leading slash)
        const firstPathElement = pathSegments[1];
        console.log(firstPathElement); // Example: "blog" if the URL is "https://www.example.com/blog/article"

        fetch(styleUrl)
            .then(res => {
                if (!res.ok) throw new Error(`Failed to load style: ${res.status}`);
                return res.json();
            })
            .then(style => {
                // 4)  use the v2 Vector Tile endpoint
                //style.sources.vectorTiles.tiles = [
                //    // Vector Tile v2 URL
                //    `https://api.tomtom.com/map/2/tile/basic/{z}/{x}/{y}.pbf?key=${apiKey}`
                //];
                style.glyphs =
                    `https://api.tomtom.com/maps/orbis/assets/fonts/0.*/{fontstack}/{range}.pbf` +
                    `?key=${apiKey}&apiVersion=1`;

                // 5) Instantiate the interactive map
                const map = new maplibregl.Map({
                    container: 'mapDiv',
                    style: style,
                    center: [0, 0],
                    zoom: 1
                });

                map.addControl(new maplibregl.NavigationControl());

                // **when the map’s style and sources are ready:**
                map.on('load', () => {

                    const pathSegments = window.location.pathname.split('/');
                    const firstPathElement = pathSegments[1];
                    // Use origin to create an absolute URL
                    const apiUrl = `${window.location.origin}/Signals/GetSignalsForMap`;

                    console.log('Loading signals from:', apiUrl);

                    fetch(apiUrl)
                        .then(res => {
                            if (!res.ok) throw new Error(`Failed to load signals: ${res.status}`);
                            return res.json();
                        })
                        .then(signals => {
                            function toNum(v) {
                                const n = parseFloat(v);
                                return Number.isFinite(n) ? n : null;
                            }
                            function isValidCoord(lon, lat) {
                                return (
                                    lon !== null &&
                                    lat !== null &&
                                    lon >= -180 && lon <= 180 &&
                                    lat >= -90 && lat <= 90
                                );
                            }
                            const valid = [], invalid = [];
                            signals.forEach(s => {
                                const lon = toNum(s.Longitude);
                                const lat = toNum(s.Latitude);
                                if (!isValidCoord(lon, lat)) {
                                    invalid.push({ id: s.SignalID, rawLon: s.Longitude, rawLat: s.Latitude });
                                } else {
                                    valid.push({
                                        SignalID: s.SignalID,
                                        PrimaryName: s.PrimaryName,
                                        SecondaryName: s.SecondaryName,
                                        Longitude: lon,
                                        Latitude: lat
                                    });
                                }
                            });
                            if (invalid.length) {
                                console.warn('Invalid signals (after parsing):', invalid);
                            }

                            const features = valid.map(s => ({
                                type: 'Feature',
                                properties: {
                                    signalId: s.SignalID,
                                    signalName: s.PrimaryName + " & " + s.SecondaryName
                                },
                                geometry: { type: 'Point', coordinates: [s.Longitude, s.Latitude] }
                            }));

                            map.addSource('signals', {
                                type: 'geojson',
                                data: { type: 'FeatureCollection', features },
                                cluster: true
                            });

                            map.addLayer({
                                id: 'signalsLayer',
                                type: 'circle',
                                source: 'signals',
                                paint: {
                                    'circle-radius': 10,
                                    'circle-color': 'purple',
                                    'circle-stroke-width': 2,
                                    'circle-stroke-color': '#fff'
                                }
                            });

                            map.addLayer({
                                id: 'cluster-count',
                                type: 'symbol',
                                source: 'signals',
                                filter: ['has', 'point_count'],
                                layout: {
                                    'text-field': ['get', 'point_count'],
                                    'text-font': ['Noto-Bold'],
                                    'text-size': 14
                                },
                                paint: {
                                    'text-color': '#fff'
                                }
                            });
                            map.on('mouseenter', 'signalsLayer', () => {
                                // add a class to the canvas so CSS switches to pointer
                                map.getCanvas().classList.add('pointer-cursor');
                            });

                            map.on('mouseleave', 'signalsLayer', () => {
                                map.getCanvas().classList.remove('pointer-cursor');
                            });
                            // Center map over signals
                            if (features.length) {
                                const bounds = features.reduce((b, f) => {
                                    return b.extend(f.geometry.coordinates);
                                }, new maplibregl.LngLatBounds(
                                    features[0].geometry.coordinates,
                                    features[0].geometry.coordinates
                                ));

                                map.fitBounds(bounds, {
                                    padding: 40,      // pixels
                                    maxZoom: 14,      // don’t zoom in too far
                                    duration: 1000    // animate for 1s
                                });
                            }

                            map.on('click', 'signalsLayer', (e) => {
                                const feature = e.features[0];
                                const props = feature.properties;

                                if (props.cluster) {
                                    // --- CLUSTER CLICK: zoom in on it ---
                                    const clusterId = props.cluster_id;
                                    map.getSource('signals').getClusterExpansionZoom(clusterId, (err, zoom) => {
                                        if (err) {
                                            console.error('cluster zoom error', err);
                                            return;
                                        }
                                        map.easeTo({
                                            center: feature.geometry.coordinates,
                                            zoom: zoom
                                        });
                                    });
                                }
                                else {
                                    displayInfobox(e, map);
                                }
                            });

                            setTimeout(() => {
                                map.resize();
                            }, 200);
                        });
                })
            })
            .catch(console.error);
    }
})(window, document);

let infobox = null;

function displayInfobox(e, map) {
    // e is the MapLibre click event on your point layer
    const feat = e.features[0];
    if (!feat) return;

    // grab your SignalID property (adjust name if yours is different)
    const signalID = feat.properties.signalId;

    // build the POST body just like before
    const tosend = { signalID };

    fetch(window.urlpathSignalInfoBox, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify(tosend)
    })
        .then(response => {
            if (!response.ok) throw new Error(`Server returned ${response.status}`);
            return response.text();            // assume your action returns HTML
        })
        .then(htmlContent => {
            // remove the old popup if it exists
            if (infobox) infobox.remove();

            infobox = new maplibregl.Popup({
                offset: [0, -10],
                closeButton: true,
                closeOnClick: false,
                anchor: 'bottom'
            })
                .setLngLat(feat.geometry.coordinates)
                .setHTML(`
                    <div><strong>Signal: ${feat.properties.signalId}</strong></div>
                    <div>${feat.properties.signalName}</div>
                `)
                .addTo(map);

            SetControlValues(signalID, null);
        })
        .catch(err => {
            console.error('displayInfobox error:', err);
            alert(`Error loading info: ${err.message}`);
        });
}

function ReportTypeChange() {
}

function RegionChange(e) {
}

//var infobox;
//function openWindow(url) {
//    var w = window.open(url, '',
//    'width=800,height=600,toolbar=0,status=0,location=0,menubar=0,directories=0,resizable=1,scrollbars=1');
//    w.focus();
//}

//function GetMap()
//{
//    map = new Microsoft.Maps.Map(document.getElementById('mapDiv'), { credentials: 'ArDqSVBgLAcobelrUlW6yVPIyL-UGPwVKTE0ce2_tAxvrZr5YFnSEFds7I1CNy5O',
//        center: new Microsoft.Maps.Location(39.50, -111.00),
//        mapTypeId: Microsoft.Maps.MapTypeId.road,
//        showDashboard: true,
//        showScalebar: false,
//        enableSearchLogo: false,
//        showMapTypeSelector: false,
//        zoom: 6,
//        customizeOverlays: false
//    });
//    var dataLayer = AddData();
//    //map.entities.push(dataLayer);
//    Microsoft.Maps.loadModule("Microsoft.Maps.Clustering", function () {
//        var clusterLayer = new Microsoft.Maps.ClusterLayer(dataLayer, {
//            clusteredPinCallback: customizeClusteredPin
//        });
//        map.layers.insert(clusterLayer);

//    });
//}

//function GetRouteMap() {
//    map = new Microsoft.Maps.Map(document.getElementById('mapDiv'), {
//        credentials: 'ArDqSVBgLAcobelrUlW6yVPIyL-UGPwVKTE0ce2_tAxvrZr5YFnSEFds7I1CNy5O',
//        center: new Microsoft.Maps.Location(39.50, -111.00),
//        mapTypeId: Microsoft.Maps.MapTypeId.road,
//        showDashboard: true,
//        showScalebar: false,
//        enableSearchLogo: false,
//        showMapTypeSelector: false,
//        zoom: 6,
//        customizeOverlays: false
//    });
//    var dataLayer = AddData();
//    //map.entities.push(dataLayer);
//    Microsoft.Maps.loadModule("Microsoft.Maps.Clustering", function () {
//        var clusterLayer = new Microsoft.Maps.ClusterLayer(dataLayer, {
//            clusteredPinCallback: customizeClusteredPin
//        });
//        map.layers.insert(clusterLayer);

//    });
//}


//function ReportTypeChange() {
//    var regionDdl = $("#Regions")[0];
//    var regionMy = document.getElementById('Regions');
//    CenterMap(regionDdl.options[regionDdl.selectedIndex].value);
//}

//function RegionChange(e) {
//    CenterMap(e.options[e.selectedIndex].value);
//    var metricsMy = document.getElementById('MetricTypes');

//}

//function CenterMap(region) {
//    if (region == 0) {
//        GetMapWithCenter(39.777584, -111.719971, 6);
//    }
//    else if (region == 1) {
//        GetMapWithCenter(41.510213, -112.015501, 8);
//    }
//    else if (region == 2) {
//        GetMapWithCenter(40.653381, -112.040634, 10);
//    }
//    else if (region == 3) {
//        GetMapWithCenter(40.354719, -110.710757, 8);
//    }
//    else if (region == 4) {
//        GetMapWithCenter(38.268951, -111.417847, 7);
//    }
//}


//function GetMapWithCenter(lat, long, zoom) {
//    map = new Microsoft.Maps.Map(document.getElementById('mapDiv'), {
//        credentials: 'ArDqSVBgLAcobelrUlW6yVPIyL-UGPwVKTE0ce2_tAxvrZr5YFnSEFds7I1CNy5O',
//        center: new Microsoft.Maps.Location(lat, long),
//        mapTypeId: Microsoft.Maps.MapTypeId.road,
//        showDashboard: true,
//        showScalebar: false,
//        enableSearchLogo: false,
//        showMapTypeSelector: false,
//        zoom: zoom,
//        customizeOverlays: false
//    });
//    var dataLayer = AddData();
//    //map.entities.push(dataLayer);
//    Microsoft.Maps.loadModule("Microsoft.Maps.Clustering", function () {
//        var clusterLayer = new Microsoft.Maps.ClusterLayer(dataLayer, {
//            clusteredPinCallback: customizeClusteredPin
//        });
//        map.layers.insert(clusterLayer);

//    });
//}


//function customizeClusteredPin(cluster) {
//    //Add click event to clustered pushpin
//    Microsoft.Maps.Events.addHandler(cluster, 'click', clusterClicked);
//}

//function clusterClicked(e) {
//    if (e.target.containedPushpins) {
//        var locs = [];
//        for (var i = 0, len = e.target.containedPushpins.length; i < len; i++) {
//            //Get the location of each pushpin.
//            locs.push(e.target.containedPushpins[i].getLocation());
//        }

//        //Create a bounding box for the pushpins.
//        var bounds = Microsoft.Maps.LocationRect.fromLocations(locs);

//        //Zoom into the bounding box of the cluster.
//        //Add a padding to compensate for the pixel area of the pushpins.
//        map.setView({ bounds: bounds, padding: 100 });
//    }
//}

//function closeInfobox() {
//    if (infobox != null) {
//        infobox.setMap(null);
//    }
//}




//function get_type(thing) {
//    if (thing === null) return "[object Null]"; // special case
//    return Object.prototype.toString.call(thing);
//}

//function Log10(val) {
//    return Math.log(val) / Math.LN10;
//}


//function ZoomIn(e) {
//    if (e.targetType == 'pushpin') {
//        var location = e.target.getLocation();
//        var pixelOffset = 0;
//        var centerpixel = map.tryLocationToPixel(location);
//        centerpixel.y = centerpixel.y - pixelOffset;
//        var newLocation = map.tryPixelToLocation(centerpixel);
//        var zoomLevel = map.getZoom();
//        if (zoomLevel < 13) zoomLevel = 13;

//        map.setView({
//            zoom: zoomLevel,
//            center: newLocation
//        });
//    }
//}

//function AddSignalFromPin(e) {
//    if (e.targetType == 'pushpin') {
//        var signalId = e.target.SignalID.toString();
//        AddSignalToList(signalId);
//    }
//}




//function displayInfobox(e) {
//    if (e.targetType == 'pushpin') {
//        actionArray = new Array();
//        var SignalID = e.target.SignalID.toString();

//        var tosend = {};
//        tosend.signalID = SignalID;
//        $.ajax({
//            url: urlpathSignalInfoBox,
//            type: "POST",
//            cache: false,
//            async: true,
//            datatype: "json",
//            contentType: "application/json; charset=utf-8",
//            data: JSON.stringify(tosend),
//            success: function (data) {
//                if (infobox != null) {
//                    infobox.setOptions({ visible: false });
//                }
//                infobox = new Microsoft.Maps.Infobox(e.target.getLocation(),
//                    { offset: new Microsoft.Maps.Point(-100, 0), htmlContent: data });
//                infobox.setMap(map);
//                SetControlValues(SignalID, null);
//            },
//            error: function (jqXHR, textStatus, errorThrown) {
//                alert(textStatus);
//            }
//        });
//    }
//}



//function CancelAsyncPostBack() {
//    if (prm.get_isInAsyncPostBack()) {
//        prm.abortPostBack();
//    }
//}

//function InitializeRequest(sender, args) {
//    if (prm.get_isInAsyncPostBack()) {
//        args.set_cancel(true);
//    }
//    postBackElement = args.get_postBackElement();
//    if (postBackElement.id == 'uxCreateChartButton') {
//        $get('UpdateProgress1').style.display = 'block';
//    }
//}
//function EndRequest(sender, args) {
//    if (postBackElement.id == 'uxCreateChartButton') {
//        $get('UpdateProgress1').style.display = 'none';
//    }
//}

//function PinFilterCheck(regionFilter, reportTypeFilter, jurisdictionFilter, areaFilter, pinRegion, pinJurisdiction, areas, pinMetricTypes) {
//    if (regionFilter != -1 && regionFilter != pinRegion) return false;
//    if (jurisdictionFilter != -1 && jurisdictionFilter != pinJurisdiction) return false;
//    if (areaFilter != -1 && areas.indexOf("," + areaFilter + ",") == -1) return false;
//    if (reportTypeFilter != -1 && pinMetricTypes.indexOf(reportTypeFilter) == -1) return false;
//    return true;
//}


