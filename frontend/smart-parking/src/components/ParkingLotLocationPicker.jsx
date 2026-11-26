import { useEffect, useRef } from "react";
import L from "leaflet";
import "leaflet/dist/leaflet.css";

const DEFAULT_CENTER = [10.7769, 106.7009];

export default function ParkingLotLocationPicker({ value, onChange }) {
  const containerRef = useRef(null);
  const mapRef = useRef(null);
  const markerRef = useRef(null);
  const onChangeRef = useRef(onChange);

  useEffect(() => { onChangeRef.current = onChange; }, [onChange]);

  useEffect(() => {
    if (!containerRef.current || mapRef.current) return undefined;
    const map = L.map(containerRef.current, {
      scrollWheelZoom: true,
      wheelDebounceTime: 80,
      updateWhenIdle: true,
      updateWhenZooming: false,
      zoomAnimation: true,
    }).setView(DEFAULT_CENTER, 12);
    const attribution = "© OpenStreetMap contributors";
    const primary = L.tileLayer(import.meta.env.VITE_MAP_TILE_URL || "https://tile.openstreetmap.de/{z}/{x}/{y}.png", {
      maxZoom: 19, attribution, crossOrigin: true, updateWhenIdle: true, updateWhenZooming: false, keepBuffer: 3,
    });
    const fallback = L.tileLayer("https://a.tile.openstreetmap.fr/osmfr/{z}/{x}/{y}.png", {
      maxZoom: 19, attribution, crossOrigin: true, updateWhenIdle: true, updateWhenZooming: false, keepBuffer: 3,
    });
    primary.on("tileerror", () => { if (!map.hasLayer(fallback)) fallback.addTo(map); });
    primary.addTo(map);
    map.attributionControl?.setPrefix(false);
    map.attributionControl?.setPosition("topright");
    map.on("click", event => onChangeRef.current({ latitude: event.latlng.lat, longitude: event.latlng.lng }));
    mapRef.current = map;
    return () => {
      map.remove();
      mapRef.current = null;
      markerRef.current = null;
    };
  }, []);

  useEffect(() => {
    const map = mapRef.current;
    if (!map || !value) return;
    const point = [Number(value.latitude), Number(value.longitude)];
    if (!point.every(Number.isFinite)) return;
    if (!markerRef.current) {
      markerRef.current = L.circleMarker(point, {
        radius: 9, color: "#fff", weight: 3, fillColor: "#1769e0", fillOpacity: 1,
      }).addTo(map);
    } else {
      markerRef.current.setLatLng(point);
    }
  }, [value?.latitude, value?.longitude]);

  return <div ref={containerRef} className="customer-map__canvas" role="application" aria-label="Bản đồ chọn vị trí bãi đỗ" style={{ height: 300, borderRadius: 10, overflow: "hidden" }} />;
}
