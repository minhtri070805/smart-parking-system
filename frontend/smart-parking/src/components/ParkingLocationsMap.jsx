import { useEffect, useRef } from "react";
import L from "leaflet";
import "leaflet/dist/leaflet.css";

const HO_CHI_MINH_CITY = [10.7769, 106.7009];

function coordinatesOf(lot) {
  const latitude = Number(lot.latitude);
  const longitude = Number(lot.longitude);
  return Number.isFinite(latitude) && Number.isFinite(longitude) &&
    latitude >= -90 && latitude <= 90 && longitude >= -180 && longitude <= 180
    ? [latitude, longitude]
    : null;
}

export default function ParkingLocationsMap({
  lots,
  selectedLotId,
  customerLocation,
  routeGeometry,
  onSelectLot,
}) {
  const containerRef = useRef(null);
  const mapRef = useRef(null);
  const parkingMarkersRef = useRef(null);
  const customerMarkerRef = useRef(null);
  const routeLayerRef = useRef(null);

  useEffect(() => {
    if (!containerRef.current || mapRef.current) return undefined;

    const map = L.map(containerRef.current, { scrollWheelZoom: true }).setView(
      HO_CHI_MINH_CITY,
      12,
    );
    L.tileLayer(
      import.meta.env.VITE_MAP_TILE_URL ||
        "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
      {
        maxZoom: 19,
        attribution:
          '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noreferrer">OpenStreetMap contributors</a>',
      },
    ).addTo(map);

    mapRef.current = map;
    parkingMarkersRef.current = L.layerGroup().addTo(map);
    return () => {
      map.remove();
      mapRef.current = null;
      parkingMarkersRef.current = null;
      customerMarkerRef.current = null;
      routeLayerRef.current = null;
    };
  }, []);

  useEffect(() => {
    const map = mapRef.current;
    const markerLayer = parkingMarkersRef.current;
    if (!map || !markerLayer) return;

    markerLayer.clearLayers();
    const plotted = [];
    for (const lot of lots) {
      const coordinates = coordinatesOf(lot);
      if (!coordinates) continue;
      const isSelected = String(lot.id) === String(selectedLotId);
      const marker = L.circleMarker(coordinates, {
        radius: isSelected ? 10 : 8,
        color: "#ffffff",
        weight: 3,
        fillColor: isSelected ? "#d83b35" : "#1769e0",
        fillOpacity: 1,
      }).addTo(markerLayer);
      marker.bindTooltip(lot.name, { direction: "top", offset: [0, -7] });
      marker.on("click", () => onSelectLot(String(lot.id)));
      plotted.push(coordinates);
    }

    if (plotted.length > 1) {
      map.fitBounds(L.latLngBounds(plotted), { padding: [36, 36], maxZoom: 13 });
    } else if (plotted.length === 1) {
      map.setView(plotted[0], 15);
    } else {
      map.setView(HO_CHI_MINH_CITY, 12);
    }
  }, [lots, selectedLotId, onSelectLot]);

  useEffect(() => {
    const map = mapRef.current;
    if (!map) return;

    if (customerMarkerRef.current) {
      map.removeLayer(customerMarkerRef.current);
      customerMarkerRef.current = null;
    }
    if (!customerLocation) return;

    const point = [customerLocation.latitude, customerLocation.longitude];
    customerMarkerRef.current = L.featureGroup([
      L.circle(point, {
        radius: Math.max(25, customerLocation.accuracy || 0),
        color: "#1976d2",
        fillColor: "#4da3ff",
        fillOpacity: 0.12,
        weight: 1,
      }),
      L.circleMarker(point, {
        radius: 8,
        color: "#ffffff",
        weight: 3,
        fillColor: "#0877e8",
        fillOpacity: 1,
      }).bindTooltip("Vị trí hiện tại của bạn"),
    ]).addTo(map);
    map.setView(point, 14);
  }, [customerLocation]);

  useEffect(() => {
    const map = mapRef.current;
    if (!map) return;
    if (routeLayerRef.current) {
      map.removeLayer(routeLayerRef.current);
      routeLayerRef.current = null;
    }
    if (!routeGeometry) return;

    routeLayerRef.current = L.geoJSON(routeGeometry, {
      style: { color: "#fa7b19", weight: 6, opacity: 0.9 },
    }).addTo(map);
    map.fitBounds(routeLayerRef.current.getBounds(), { padding: [42, 42] });
  }, [routeGeometry]);

  return (
    <div
      ref={containerRef}
      className="customer-map__canvas"
      role="application"
      aria-label="Bản đồ các bãi đỗ xe"
    />
  );
}
