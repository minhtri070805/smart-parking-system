import { useCallback, useEffect, useRef, useState, useSyncExternalStore } from "react";
import { Link, useSearchParams } from "react-router-dom";
import RequireCustomer from "../components/RequireCustomer.jsx";
import ParkingLocationsMap from "../components/ParkingLocationsMap.jsx";
import ParkingSlotManagement from "./ParkingSlotManagement.jsx";
import { getAuthSession, subscribeAuthSession } from "../services/authSession.js";
import { apiRequest } from "../services/api.js";
import { formatVietnameseCarPlateInput, validateVietnameseCarPlate } from "../services/plateNumber.js";
import { collectPages } from "../services/staffApi.js";
import "../workspace.css";

function readData(response) {
  return response?.data;
}

function readItems(response) {
  const data = readData(response);
  return Array.isArray(data) ? data : data?.items || [];
}

function localDateTimeValue(date = new Date()) {
  return new Date(date.getTime() - date.getTimezoneOffset() * 60000)
    .toISOString()
    .slice(0, 16);
}

const slotLabels = {
  EMPTY: "Có thể đặt",
  OCCUPIED: "Đang sử dụng",
  MAINTENANCE: "Đang bảo trì",
  UNAVAILABLE: "Không khả dụng",
  RESERVED: "Đã đặt / đang giữ chỗ",
};

const bookingStatusLabels = {
  PENDING: "Chờ xe đến",
  CHECKED_IN: "Đã qua cổng · chờ điều phối",
  PARKED: "Đang đỗ",
  CANCELLED: "Đã hủy",
  EXPIRED: "Đã hết hạn",
};

function DashboardContent({ publicView = false }) {
  const [params] = useSearchParams();
  const floorLotIdRef = useRef(params.get("lotId") || "");
  const [revision, setRevision] = useState(0);
  const [lots, setLots] = useState([]);
  const [selectedLotId, setSelectedLotId] = useState(params.get("lotId") || "");
  const [floors, setFloors] = useState([]);
  const [selectedFloorId, setSelectedFloorId] = useState(params.get("floorId") || "");
  const [slots, setSlots] = useState([]);
  const [slotsUpdatedAt, setSlotsUpdatedAt] = useState(null);
  const [lotsLoading, setLotsLoading] = useState(true);
  const [floorsLoading, setFloorsLoading] = useState(false);
  const [slotsLoading, setSlotsLoading] = useState(false);
  const [lotsError, setLotsError] = useState("");
  const [customerLocation, setCustomerLocation] = useState(null);
  const [locationError, setLocationError] = useState("");
  const [locationLoading, setLocationLoading] = useState(false);
  const [routeGeometry, setRouteGeometry] = useState(null);
  const [routeSummary, setRouteSummary] = useState("");
  const [routeLoading, setRouteLoading] = useState(false);
  const [routeError, setRouteError] = useState("");
  const [floorsError, setFloorsError] = useState("");
  const [slotsError, setSlotsError] = useState("");
  const [capacity, setCapacity] = useState(null);
  const [capacityLoading, setCapacityLoading] = useState(false);
  const [capacityError, setCapacityError] = useState("");
  const [capacityUpdatedAt, setCapacityUpdatedAt] = useState(null);
  const [myBookings, setMyBookings] = useState([]);
  const [myBookingsLoading, setMyBookingsLoading] = useState(false);
  const [myBookingsError, setMyBookingsError] = useState("");
  const [cancellingBookingId, setCancellingBookingId] = useState(null);
  const [bookingCancelError, setBookingCancelError] = useState("");
  const [bookingOpen, setBookingOpen] = useState(false);
  const [bookingProfile, setBookingProfile] = useState(null);
  const [bookingVehicles, setBookingVehicles] = useState([]);
  const [selectedVehicleId, setSelectedVehicleId] = useState("");
  const [bookingUseCustomPlate, setBookingUseCustomPlate] = useState(false);
  const [bookingDetailsLoading, setBookingDetailsLoading] = useState(false);
  const [bookingDetailsError, setBookingDetailsError] = useState("");
  const [bookingDetailsRevision, setBookingDetailsRevision] = useState(0);
  const [vehiclePickerOpen, setVehiclePickerOpen] = useState(false);
  const [bookingPlateNumber, setBookingPlateNumber] = useState("");
  const [bookingStartTime, setBookingStartTime] = useState("");
  const [bookingStartTimeChanged, setBookingStartTimeChanged] = useState(false);
  const [bookingNotes, setBookingNotes] = useState("");
  const [bookingSaving, setBookingSaving] = useState(false);
  const [bookingError, setBookingError] = useState("");
  const [bookingMessage, setBookingMessage] = useState("");
  const [bookingPaymentResult, setBookingPaymentResult] = useState("");

  useEffect(() => {
    const result = params.get("bookingPayment");
    if (!result) return;
    const bookingId = params.get("bookingId");
    setBookingPaymentResult(result);
    setRevision((current) => current + 1);
    window.history.replaceState({}, "", window.location.pathname);
    if (result === "success") {
      setBookingMessage(`Thanh toán cọc cho vé #${bookingId || ""} thành công. Hóa đơn đã được thêm vào mục Vé của tôi.`);
    } else if (result === "processing") {
      setBookingMessage(`VNPay đã nhận thanh toán cho vé #${bookingId || ""}; hệ thống đang đồng bộ trạng thái hóa đơn.`);
    } else {
      setBookingMessage(`Thanh toán cho vé #${bookingId || ""} chưa hoàn tất. Vé chưa được xác nhận thanh toán.`);
    }
  }, [params]);

  const loadLots = useCallback(async () => {
    setLotsLoading(true);
    setLotsError("");
    try {
      const items = await collectPages(
        async (page) =>
          (
            await apiRequest(
              `/v1/parking-lots?pageNumber=${page}&pageSize=100&isActive=true`,
              { skipAuth: true },
            )
          ).data,
      );
      setLots(items);
      setSelectedLotId((current) =>
        items.some((lot) => String(lot.id) === current)
          ? current
          : String(items[0]?.id || ""),
      );
    } catch (error) {
      setLotsError(error.message);
    } finally {
      setLotsLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadLots();
  }, [loadLots]);

  useEffect(() => {
    const lotChanged = floorLotIdRef.current !== selectedLotId;
    floorLotIdRef.current = selectedLotId;
    if (!selectedLotId) {
      setFloors([]);
      setSelectedFloorId("");
      setSlots([]);
      return;
    }

    let active = true;
    const loadFloors = async () => {
      setFloorsLoading(true);
      setFloorsError("");
      if (lotChanged) {
        setFloors([]);
        setSlots([]);
        setSelectedFloorId("");
      }
      try {
        const response = await apiRequest(
          `/v1/parking-lots/${selectedLotId}/floors?isActiveOnly=true`,
          { skipAuth: true },
        );
        if (!active) return;
        const items = readItems(response);
        setFloors(items);
        setSelectedFloorId((current) =>
          items.some((floor) => String(floor.id) === current)
            ? current
            : String(items[0]?.id || ""),
        );
      } catch (error) {
        if (active) setFloorsError(error.message);
      } finally {
        if (active) setFloorsLoading(false);
      }
    };

    void loadFloors();
    return () => {
      active = false;
    };
  }, [selectedLotId, revision]);

  useEffect(() => {
    if (!selectedLotId) {
      setCapacity(null);
      setCapacityError("");
      setCapacityLoading(false);
      return undefined;
    }

    let active = true;
    const controller = new AbortController();
    const loadCapacity = async () => {
      try {
        const response = await apiRequest(
          `/v1/parking-lots/${selectedLotId}/capacity`,
          { skipAuth: true, signal: controller.signal },
        );
        if (!active) return;
        const result = response?.data;
        if (!result || result.lotId !== Number(selectedLotId))
          throw new Error("Dữ liệu sức chứa bãi không hợp lệ.");
        setCapacity(result);
        setCapacityError("");
        setCapacityUpdatedAt(new Date());
      } catch (error) {
        if (active && !controller.signal.aborted)
          setCapacityError(error.message);
      } finally {
        if (active) setCapacityLoading(false);
      }
    };

    setCapacityLoading(capacity?.lotId !== Number(selectedLotId));
    void loadCapacity();
    return () => {
      active = false;
      controller.abort();
    };
  }, [selectedLotId, revision]);

  useEffect(() => {
    if (publicView) return undefined;

    let active = true;
    const controller = new AbortController();
    const loadMyBookings = async () => {
      try {
        const bookings = await collectPages(
          async (page) =>
            (
              await apiRequest(
                `/v1/bookings/my?pageNumber=${page}&pageSize=20`,
                { signal: controller.signal },
              )
            ).data,
        );
        if (active) {
          setMyBookings(bookings);
          setMyBookingsError("");
        }
      } catch (error) {
        if (active && !controller.signal.aborted)
          setMyBookingsError(error.message);
      } finally {
        if (active) setMyBookingsLoading(false);
      }
    };

    setMyBookingsLoading(myBookings.length === 0);
    void loadMyBookings();
    return () => {
      active = false;
      controller.abort();
    };
  }, [publicView, revision]);

  useEffect(() => {
    if (!selectedFloorId) {
      setSlots([]);
      return;
    }

    let active = true;
    const loadSlots = async () => {
      setSlotsLoading(true);
      setSlotsError("");
      try {
        const items = await collectPages(
          async (page) =>
            (
              await apiRequest(
                `/v1/parking-floors/${selectedFloorId}/slots?pageNumber=${page}&pageSize=100`,
                { skipAuth: true },
              )
            ).data,
        );
        if (active) {
          setSlots(items);
          setSlotsUpdatedAt(new Date());
        }
      } catch (error) {
        if (active) setSlotsError(error.message);
      } finally {
        if (active) setSlotsLoading(false);
      }
    };

    void loadSlots();
    return () => {
      active = false;
    };
  }, [selectedFloorId, revision]);

  useEffect(() => {
    if (!bookingOpen || publicView) return undefined;

    let active = true;
    const loadBookingDetails = async () => {
      setBookingDetailsLoading(true);
      setBookingDetailsError("");
      try {
        const [profileResponse, vehicles] = await Promise.all([
          apiRequest("/v1/users/me"),
          collectPages(
            async (page) =>
              (
                await apiRequest(
                  `/v1/vehicles?page=${page}&pageSize=100`,
                )
              ).data,
          ),
        ]);
        if (!active) return;

        const availableVehicles = vehicles.filter(
          (vehicle) => vehicle.isActive !== false,
        );
        setBookingProfile(profileResponse?.data || null);
        setBookingVehicles(availableVehicles);
        setSelectedVehicleId(
          availableVehicles.length === 1
            ? String(availableVehicles[0].id)
            : "",
        );
      } catch (error) {
        if (active) setBookingDetailsError(error.message);
      } finally {
        if (active) setBookingDetailsLoading(false);
      }
    };

    void loadBookingDetails();
    return () => {
      active = false;
    };
  }, [bookingOpen, bookingDetailsRevision, publicView]);

  const availableCount = slots.filter(
    (slot) => slot.physicalStatus === "EMPTY" && slot.isAvailable,
  ).length;
  const maintenanceCount = slots.filter(
    (slot) => slot.physicalStatus === "MAINTENANCE",
  ).length;
  const occupiedCount = slots.filter(
    (slot) => slot.physicalStatus === "OCCUPIED",
  ).length;
  const selectedLot = lots.find((lot) => String(lot.id) === selectedLotId);
  const selectedFloor = floors.find(
    (floor) => String(floor.id) === selectedFloorId,
  );
  const selectedVehicle = bookingVehicles.find(
    (vehicle) => !bookingUseCustomPlate && String(vehicle.id) === selectedVehicleId,
  );
  const currentCapacity =
    capacity?.lotId === Number(selectedLotId) ? capacity : null;
  const vehicleName = (vehicle) =>
    [vehicle.brand, vehicle.model].filter(Boolean).join(" ") ||
    `Xe cá nhân`;
  const useCurrentLocation = () => {
    setLocationError("");
    if (!navigator.geolocation) {
      setLocationError("Trình duyệt này không hỗ trợ xác định vị trí.");
      return;
    }
    setLocationLoading(true);
    navigator.geolocation.getCurrentPosition(
      ({ coords }) => {
        setCustomerLocation({ latitude: coords.latitude, longitude: coords.longitude, accuracy: coords.accuracy });
        setLocationLoading(false);
      },
      (error) => {
        setLocationError(
          error.code === error.PERMISSION_DENIED
            ? "Bạn chưa cho phép truy cập vị trí. Hãy bật quyền Vị trí cho trang web trong cài đặt trình duyệt rồi thử lại."
            : error.code === error.TIMEOUT
              ? "Trình duyệt mất quá nhiều thời gian để xác định vị trí. Hãy bật GPS hoặc thử lại ngoài trời."
              : "Không đọc được vị trí hiện tại. Kiểm tra GPS và quyền vị trí của trình duyệt rồi thử lại.",
        );
        setLocationLoading(false);
      },
      { enableHighAccuracy: true, timeout: 10000, maximumAge: 60000 },
    );
  };

  const showRouteToLot = async () => {
    setRouteError("");
    setRouteSummary("");
    setRouteGeometry(null);
    if (!customerLocation) {
      setRouteError("Hãy xác định vị trí hiện tại trước để xem đường đi trên bản đồ.");
      return;
    }
    if (!selectedLot?.latitude || !selectedLot?.longitude) {
      setRouteError("Bãi này chưa có tọa độ bản đồ.");
      return;
    }
    setRouteLoading(true);
    try {
      const url = `https://router.project-osrm.org/route/v1/driving/${customerLocation.longitude},${customerLocation.latitude};${selectedLot.longitude},${selectedLot.latitude}?overview=full&geometries=geojson`;
      const response = await fetch(url);
      if (!response.ok) throw new Error("Không kết nối được dịch vụ tìm đường.");
      const result = await response.json();
      const route = result.code === "Ok" ? result.routes?.[0] : null;
      if (!route) throw new Error("Không tìm thấy đường đi giữa vị trí của bạn và bãi.");
      setRouteGeometry(route.geometry);
      setRouteSummary(`Khoảng ${(route.distance / 1000).toFixed(1)} km · ${Math.ceil(route.duration / 60)} phút lái xe`);
    } catch (error) {
      setRouteError(error.message || "Không thể tìm đường lúc này. Hãy thử lại.");
    } finally {
      setRouteLoading(false);
    }
  };

  const openBookingForm = () => {
    setBookingError("");
    setBookingMessage("");
    setBookingPaymentResult("");
    setBookingProfile(null);
    setBookingVehicles([]);
    setSelectedVehicleId("");
    setBookingUseCustomPlate(false);
    setBookingDetailsError("");
    setVehiclePickerOpen(false);
    const startTime = new Date();
    setBookingStartTime(localDateTimeValue(startTime));
    setBookingStartTimeChanged(false);
    setBookingOpen(true);
    window.setTimeout(() => {
      document.getElementById("customer-booking-form")?.scrollIntoView({
        behavior: "smooth",
        block: "start",
      });
    }, 0);
  };

  const submitBooking = async (event) => {
    event.preventDefault();
    setBookingSaving(true);
    setBookingError("");
    setBookingMessage("");

    try {
      const startTime = bookingStartTimeChanged
        ? new Date(bookingStartTime)
        : new Date(bookingStartTime);
      if (startTime && startTime.getTime() <= Date.now()) {
        throw new Error(
          "Thời gian bắt đầu đã qua. Vui lòng chọn thời gian hiện tại hoặc trong tương lai.",
        );
      }
      if (bookingVehicles.length > 0 && !selectedVehicle && !bookingUseCustomPlate) {
        throw new Error("Vui lòng chọn xe bạn sẽ sử dụng.");
      }
      const plateNumber = bookingUseCustomPlate || !selectedVehicle
        ? validateVietnameseCarPlate(bookingPlateNumber)
        : undefined;
      const response = await apiRequest("/v1/bookings", {
        method: "POST",
        body: JSON.stringify({
          lotId: Number(selectedLotId),
          ...(!bookingUseCustomPlate && selectedVehicle
            ? { vehicleId: selectedVehicle.id }
            : { plateNumber }),
          startTime: startTime.toISOString(),
          paymentMode: "PREPAY",
          notes: bookingNotes.trim() || null,
        }),
      });
      if (response?.data?.paymentUrl) {
        window.location.assign(response.data.paymentUrl);
        return;
      }
      setBookingMessage(
        response?.data?.id
          ? `Đặt chỗ tại ${selectedLot?.name || "bãi đỗ"} thành công. Bạn thanh toán tại quầy khi checkout. Khi đến nơi, hãy đỗ tại một ô còn trống theo hướng dẫn của nhân viên. Mã đặt chỗ #${response.data.id}.`
          : response?.message ||
              `Đặt chỗ tại ${selectedLot?.name || "bãi đỗ"} thành công.`,
      );
      setBookingOpen(false);
      setBookingPlateNumber("");
      setBookingNotes("");
      setRevision((value) => value + 1);
    } catch (error) {
      setBookingError(error.message);
    } finally {
      setBookingSaving(false);
    }
  };

  const cancelBooking = async (bookingId) => {
    setCancellingBookingId(bookingId);
    setBookingCancelError("");
    try {
      const response = await apiRequest(`/v1/bookings/${bookingId}/cancel`, {
        method: "POST",
        body: JSON.stringify({}),
      });
      if (response?.data?.status !== "CANCELLED") {
        throw new Error(
          response?.message || "Chưa xác nhận được việc hủy chỗ. Vui lòng tải lại.",
        );
      }
      setMyBookings((current) =>
        current.map((booking) =>
          booking.id === bookingId ? response.data : booking,
        ),
      );
      setRevision((value) => value + 1);
    } catch (error) {
      setBookingCancelError(error.message);
    } finally {
      setCancellingBookingId(null);
    }
  };

  return (
    <section className="customer-page container">
      <div className="customer-card">
        <div className="customer-card__heading">
          <div>
            <h2>Chọn bãi và tầng</h2>
            <p className="muted">
              Danh sách chỉ bao gồm các bãi đang hoạt động.
            </p>
          </div>
          <div className="customer-page__actions">
            {selectedLotId && selectedFloorId && <a className="btn btn--outline" href={`/app/parking?lotId=${encodeURIComponent(selectedLotId)}&floorId=${encodeURIComponent(selectedFloorId)}`}>Xem tầng này bằng 3D</a>}
            <Link to={publicView ? "/login" : "/customer/sessions"} className="btn btn--primary">
              {publicView ? "Đăng nhập để xem phiên của tôi" : "Phiên gửi xe của tôi"}
            </Link>
            <button
              type="button"
              className="btn btn--outline"
              onClick={() => {
                void loadLots();
                setRevision((n) => n + 1);
              }}
              disabled={lotsLoading || floorsLoading || slotsLoading}
            >
              {lotsLoading ? "Đang tải..." : "Làm mới"}
            </button>
          </div>
        </div>

        {lotsError && (
          <p className="customer-message customer-message--error" role="alert">
            {lotsError}
          </p>
        )}
        {slotsUpdatedAt && selectedFloor && (
          <span>
            Trạng thái ô {selectedFloor.name} được làm mới lúc{" "}
            {slotsUpdatedAt.toLocaleTimeString("vi-VN", {
              hour: "2-digit",
              minute: "2-digit",
            })}
            .
          </span>
        )}

        {!lotsLoading && !lotsError && lots.length === 0 && (
          <p className="customer-message">
            Hiện chưa có bãi đỗ nào đang hoạt động.
          </p>
        )}

        {lots.length > 0 && (
          <div className="customer-select-grid">
            <label>
              Bãi đỗ xe
              <select
                value={selectedLotId}
                onChange={(event) => {
                  setSelectedLotId(event.target.value);
                  setBookingOpen(false);
                  setBookingError("");
                  setBookingMessage("");
                }}
              >
                {lots.map((lot) => (
                  <option value={lot.id} key={lot.id}>
                    {lot.name}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Tầng
              <select
                value={selectedFloorId}
                onChange={(event) => setSelectedFloorId(event.target.value)}
                disabled={floorsLoading || floors.length === 0}
              >
                {floors.map((floor) => (
                  <option value={floor.id} key={floor.id}>
                    {floor.name}
                  </option>
                ))}
              </select>
            </label>
          </div>
        )}

        {selectedLot && (
          <section className="customer-map" aria-labelledby="customer-map-title">
            <div className="customer-map__heading">
              <div>
                <h3 id="customer-map-title">Vị trí bãi đỗ</h3>
                <p className="muted">
                  {selectedLot.address || "Bãi chưa có địa chỉ để hiển thị trên bản đồ."}
                </p>
              </div>
              <div className="customer-map__actions">
                <button
                  type="button"
                  className="btn btn--outline"
                  onClick={useCurrentLocation}
                  disabled={locationLoading}
                >
                  {locationLoading ? "Đang xác định vị trí..." : customerLocation ? "Cập nhật vị trí của tôi" : "Dùng vị trí của tôi"}
                </button>
                <button
                  type="button"
                  className="btn btn--primary"
                  onClick={showRouteToLot}
                  disabled={routeLoading || !selectedLot.latitude || !selectedLot.longitude}
                >
                  {routeLoading ? "Đang tìm đường..." : "Chỉ đường trên bản đồ"}
                </button>
              </div>
            </div>
            {locationError && (
              <p className="customer-message customer-message--error" role="status">
                {locationError}
              </p>
            )}
            {routeError && (
              <p className="customer-message customer-message--error" role="status">{routeError}</p>
            )}
            {routeSummary && <p className="customer-map__route-summary">{routeSummary}</p>}
            <ParkingLocationsMap
              lots={lots}
              selectedLotId={selectedLotId}
              customerLocation={customerLocation}
              routeGeometry={routeGeometry}
              onSelectLot={setSelectedLotId}
            />
          </section>
        )}

        {floorsLoading && (
          <p className="customer-message">Đang tải danh sách tầng...</p>
        )}
        {floorsError && (
          <p className="customer-message customer-message--error" role="alert">
            {floorsError}
          </p>
        )}
        {!floorsLoading &&
          !floorsError &&
          selectedLotId &&
          floors.length === 0 && (
            <p className="customer-message">
              Bãi này chưa có tầng đang hoạt động.
            </p>
          )}
      </div>

      {selectedLot && (
        <div className="customer-card">
          <div className="customer-card__heading">
            <div>
              <h2>
                {selectedLot.name}
                {selectedFloor ? ` · ${selectedFloor.name}` : ""}
              </h2>
              <p className="muted">
                {selectedLot.address || "Chưa có địa chỉ bãi đỗ"}
              </p>
            </div>
            {!publicView && (
              <button
                type="button"
                className="btn btn--primary"
                onClick={bookingOpen ? () => setBookingOpen(false) : openBookingForm}
                disabled={
                  bookingSaving ||
                  (!bookingOpen &&
                    currentCapacity &&
                    currentCapacity.availableForBooking <= 0)
                }
                aria-expanded={bookingOpen}
                aria-controls="customer-booking-form"
              >
                {bookingOpen
                  ? "Đóng form đặt chỗ"
                  : currentCapacity && currentCapacity.availableForBooking <= 0
                    ? "Hết chỗ đặt trước"
                    : "Đặt chỗ tại bãi này"}
              </button>
            )}
          </div>

          {bookingMessage && (
            <div className={`customer-message ${bookingPaymentResult === "failed" ? "customer-message--error" : "customer-message--success"}`} role="status">
              <span>{bookingMessage}</span>
              {bookingPaymentResult && <Link className="text-button" to="/customer/my-tickets">Xem hóa đơn trong Vé của tôi</Link>}
            </div>
          )}

          {bookingOpen && !publicView && (
            <form
              id="customer-booking-form"
              className="customer-booking-form customer-form"
              onSubmit={(event) => void submitBooking(event)}
            >
              <div className="customer-booking-form__heading">
                <h3>Thông tin đặt chỗ</h3>
                <p className="muted">
                  Đặt chỗ giữ suất tại bãi, chưa gán ô cụ thể. Khi đến nơi, bạn đỗ tại một ô còn trống theo hướng dẫn của nhân viên. Thông tin liên hệ và xe đã đăng ký sẽ được điền tự động.
                </p>
              </div>
              {bookingDetailsLoading ? (
                <p className="customer-message">
                  Đang tải hồ sơ và danh sách xe...
                </p>
              ) : bookingDetailsError ? (
                <div
                  className="customer-message customer-message--error"
                  role="alert"
                >
                  <span>{bookingDetailsError}</span>
                  <button
                    type="button"
                    className="text-button"
                    onClick={() =>
                      setBookingDetailsRevision((revision) => revision + 1)
                    }
                  >
                    Tải lại thông tin
                  </button>
                </div>
              ) : (
                <>
                  <div className="customer-booking-profile">
                    <h4>Thông tin khách hàng</h4>
                    <div className="customer-booking-profile__fields">
                      <label>
                        Họ và tên
                        <input
                          value={bookingProfile?.fullName || ""}
                          readOnly
                          placeholder="Chưa cập nhật họ tên"
                        />
                      </label>
                      <label>
                        Email
                        <input
                          value={bookingProfile?.email || ""}
                          readOnly
                          placeholder="Chưa cập nhật email"
                        />
                      </label>
                      <label>
                        Số điện thoại
                        <input
                          value={bookingProfile?.phoneNumber || ""}
                          readOnly
                          placeholder="Chưa cập nhật số điện thoại"
                        />
                      </label>
                    </div>
                  </div>

                  {bookingVehicles.length > 0 && (
                    <div className="customer-booking-vehicle-mode" aria-label="Chọn cách nhập biển số xe">
                      <button
                        type="button"
                        aria-pressed={!bookingUseCustomPlate}
                        onClick={() => setBookingUseCustomPlate(false)}
                      >
                        Dùng xe đã đăng ký
                      </button>
                      <button
                        type="button"
                        aria-pressed={bookingUseCustomPlate}
                        onClick={() => {
                          setBookingUseCustomPlate(true);
                          setSelectedVehicleId("");
                        }}
                      >
                        Nhập biển số xe khác
                      </button>
                    </div>
                  )}

                  {bookingVehicles.length === 1 && !bookingUseCustomPlate && (
                    <div className="customer-booking-vehicle">
                      <span>Xe sử dụng</span>
                      <strong>{vehicleName(bookingVehicles[0])}</strong>
                      <div className="customer-booking-vehicle__details">
                        <span>
                          Biển số <strong>{bookingVehicles[0].plateNumber}</strong>
                        </span>
                        {bookingVehicles[0].color && (
                          <span>
                            Màu xe <strong>{bookingVehicles[0].color}</strong>
                          </span>
                        )}
                      </div>
                    </div>
                  )}

                  {bookingVehicles.length > 1 && !bookingUseCustomPlate && (
                    <div className="customer-booking-vehicle-picker">
                      <span className="customer-booking-vehicle-picker__label">
                        Chọn xe bạn sẽ sử dụng
                      </span>
                      <button
                        type="button"
                        className="customer-booking-vehicle-picker__trigger"
                        aria-haspopup="listbox"
                        aria-expanded={vehiclePickerOpen}
                        onClick={() =>
                          setVehiclePickerOpen((isOpen) => !isOpen)
                        }
                      >
                        {selectedVehicle ? (
                          <span>
                            <strong>{vehicleName(selectedVehicle)}</strong>
                            <small>{selectedVehicle.plateNumber}</small>
                          </span>
                        ) : (
                          <span>Chọn xe của bạn</span>
                        )}
                        <span aria-hidden="true">
                          {vehiclePickerOpen ? "▲" : "▼"}
                        </span>
                      </button>
                      {vehiclePickerOpen && (
                        <div
                          className="customer-booking-vehicle-picker__options"
                          role="listbox"
                          aria-label="Danh sách xe của bạn"
                        >
                          {bookingVehicles.map((vehicle) => (
                            <button
                              key={vehicle.id}
                              type="button"
                              role="option"
                              aria-selected={
                                String(vehicle.id) === selectedVehicleId
                              }
                              onClick={() => {
                                setSelectedVehicleId(String(vehicle.id));
                                setVehiclePickerOpen(false);
                              }}
                            >
                              <strong>{vehicleName(vehicle)}</strong>
                              <small>{vehicle.plateNumber}</small>
                            </button>
                          ))}
                        </div>
                      )}
                    </div>
                  )}

                  {(bookingVehicles.length === 0 || bookingUseCustomPlate) && (
                    <label className="customer-booking-form__plate">
                      {bookingUseCustomPlate ? "Biển số xe khác" : "Biển số xe"}
                      <input
                        type="text"
                        name="plateNumber"
                        value={bookingPlateNumber}
                        onChange={(event) =>
                          setBookingPlateNumber(formatVietnameseCarPlateInput(event.target.value))
                        }
                        placeholder="Ví dụ: 51F-123.45"
                        pattern="[0-9]{2}[A-Za-z][ -]?[0-9]{3}[.]?[0-9]{2}"
                        title="Nhập theo dạng biển số ô tô Việt Nam, ví dụ 51F-123.45"
                        maxLength={12}
                        required
                      />
                    </label>
                  )}
                  {selectedVehicle && bookingVehicles.length > 1 && (
                    <div className="customer-booking-vehicle">
                      <span>Xe đã chọn</span>
                      <strong>{vehicleName(selectedVehicle)}</strong>
                      <div className="customer-booking-vehicle__details">
                        <span>
                          Biển số <strong>{selectedVehicle.plateNumber}</strong>
                        </span>
                        {selectedVehicle.color && (
                          <span>
                            Màu xe <strong>{selectedVehicle.color}</strong>
                          </span>
                        )}
                      </div>
                    </div>
                  )}
                </>
              )}
              <label>
                Thời gian bắt đầu
                <input
                  type="datetime-local"
                  name="startTime"
                  value={bookingStartTime}
                  min={localDateTimeValue()}
                  onChange={(event) => {
                    setBookingStartTime(event.target.value);
                    setBookingStartTimeChanged(true);
                  }}
                  required
                />
              </label>
              <label className="customer-booking-form__notes">
                Ghi chú (không bắt buộc)
                <textarea
                  name="notes"
                  value={bookingNotes}
                  onChange={(event) => setBookingNotes(event.target.value)}
                  maxLength={500}
                  rows={3}
                />
              </label>
              {bookingError && (
                <p className="customer-message customer-message--error" role="alert">
                  {bookingError}
                </p>
              )}
              <div className="customer-booking-form__actions">
                <button
                  type="submit"
                  className="btn btn--primary"
                  disabled={
                    bookingSaving ||
                    bookingDetailsLoading ||
                    Boolean(bookingDetailsError)
                  }
                >
                  {bookingSaving ? "Đang chuyển đến VNPay..." : "Đặt chỗ & thanh toán cọc"}
                </button>
                <button
                  type="button"
                  className="btn btn--outline"
                  onClick={() => setBookingOpen(false)}
                  disabled={bookingSaving}
                >
                  Hủy
                </button>
              </div>
            </form>
          )}

          <div className="customer-lot-summary">
            <div>
              <strong>{currentCapacity?.availableForBooking ?? "—"}</strong>
              <span>Chỗ có thể nhận xe toàn bãi</span>
            </div>
            <div>
              <strong>{currentCapacity?.totalCapacity ?? "—"}</strong>
              <span>Tổng sức chứa</span>
            </div>
          </div>
          <div className="customer-capacity-meta" aria-live="polite">
            {capacityLoading && !currentCapacity && (
              <span>Đang tải sức chứa thực tế...</span>
            )}
            {currentCapacity && (
              <span>
                Đã tính cả {currentCapacity.pendingBookingsCount} chỗ
                đang được booking giữ.
                {capacityUpdatedAt &&
                  ` Cập nhật lúc ${capacityUpdatedAt.toLocaleTimeString("vi-VN", { hour: "2-digit", minute: "2-digit" })}.`}
              </span>
            )}
            {capacityError && (
              <span className="customer-message--error" role="alert">
                Không tải được số chỗ có thể nhận xe: {capacityError}
              </span>
            )}
          </div>
          {currentCapacity?.availableForBooking <= 0 &&
            slots.some(
              (slot) =>
                slot.physicalStatus === "EMPTY" &&
                slot.isAvailable &&
                slot.isBookingEligible === false,
            ) && (
              <p className="customer-message" role="note">
                Tầng này có ô trống vật lý trong khu không nhận đặt chỗ vãng lai
                (ví dụ khu cố định / vé tháng). Đặt trước chỉ nhận ở khu vãng lai
                (TRANSIENT).
              </p>
            )}

          {selectedFloor && (
            <>
              {slotsError && (
                <p
                  className="customer-message customer-message--error"
                  role="alert"
                >
                  {slotsError}
                </p>
              )}
              {slotsLoading ? (
                <p className="customer-message">
                  Đang tải tình trạng vị trí...
                </p>
              ) : !slotsError ? (
                <>
                  <div
                    className="customer-stat-grid"
                    aria-label="Tổng quan tầng"
                  >
                    <div>
                      <strong>{availableCount}</strong>
                      <span>Có thể đặt</span>
                    </div>
                    <div>
                      <strong>{occupiedCount}</strong>
                      <span>Đang sử dụng</span>
                    </div>
                    <div>
                      <strong>{maintenanceCount}</strong>
                      <span>Đang bảo trì</span>
                    </div>
                    <div>
                      <strong>{slots.length}</strong>
                      <span>Tổng vị trí</span>
                    </div>
                  </div>

                  {slots.length === 0 ? (
                    <p className="customer-message">
                      Tầng này chưa có dữ liệu vị trí.
                    </p>
                  ) : (
                    <ul
                      className="customer-slot-grid"
                      aria-label="Danh sách vị trí đỗ xe"
                    >
                      {slots.map((slot) => {
                        const effectiveStatus =
                          slot.physicalStatus === "EMPTY" && !slot.isAvailable
                            ? slot.isActive ? "RESERVED" : "UNAVAILABLE"
                            : slot.physicalStatus;
                        const status =
                          slot.physicalStatus === "EMPTY" &&
                          slot.isBookingEligible === false
                            ? `${slot.zoneName || "Khu này"} · không nhận đặt trước`
                            : slotLabels[effectiveStatus] || effectiveStatus;
                        const selectable =
                          !publicView &&
                          slot.physicalStatus === "EMPTY" &&
                          slot.isAvailable &&
                          slot.isActive;
                        return (
                          <li key={slot.id}>
                            <div className={`customer-slot customer-slot--${String(effectiveStatus).toLowerCase()}`} aria-label={`${slot.slotCode}: ${selectable ? "Còn trống" : status}`}>
                              <strong>{slot.slotCode}</strong>
                              <span>{selectable ? "Còn trống" : status}</span>
                            </div>
                          </li>
                        );
                      })}
                    </ul>
                  )}
                </>
              ) : null}
            </>
          )}
        </div>
      )}

      {!publicView && (
        <section className="customer-card" aria-labelledby="my-bookings-title">
          <div className="customer-card__heading">
            <div>
              <h2 id="my-bookings-title">Đặt chỗ của tôi</h2>
              <p className="muted">
                Các lượt đặt chỗ được lưu trong tài khoản, tại mọi bãi.
              </p>
            </div>
            <button
              type="button"
              className="btn btn--outline"
              onClick={() => setRevision((value) => value + 1)}
              disabled={myBookingsLoading}
            >
              {myBookingsLoading ? "Đang tải..." : "Làm mới đặt chỗ"}
            </button>
          </div>
          {myBookingsError && (
            <p className="customer-message customer-message--error" role="alert">
              Không tải được đặt chỗ: {myBookingsError}
            </p>
          )}
          {bookingCancelError && (
            <p className="customer-message customer-message--error" role="alert">
              Không hủy được chỗ: {bookingCancelError}
            </p>
          )}
          {myBookingsLoading && myBookings.length === 0 && (
            <p className="customer-message">Đang tải đặt chỗ của bạn...</p>
          )}
          {!myBookingsLoading && !myBookingsError && myBookings.length === 0 && (
            <p className="customer-message">Bạn chưa có lượt đặt chỗ nào.</p>
          )}
          {myBookings.length > 0 && (
            <ul className="customer-booking-list" aria-label="Đặt chỗ của tôi">
              {myBookings.map((booking) => (
                <li className="customer-booking-item" key={booking.id}>
                  <div className="customer-booking-item__heading">
                    <div>
                      <strong>{booking.plateNumber}</strong>
                      <span>
                        {booking.lotName || `Bãi #${booking.lotId}`}
                        {booking.lotAddress ? ` · ${booking.lotAddress}` : ""}
                      </span>
                    </div>
                    <span
                      className={`customer-booking-status customer-booking-status--${String(booking.status).toLowerCase()}`}
                    >
                      {bookingStatusLabels[booking.status] || booking.status}
                    </span>
                  </div>
                  <dl>
                    <div>
                      <dt>Mã đặt chỗ</dt>
                      <dd>#{booking.id}</dd>
                    </div>
                    <div>
                      <dt>Dự kiến vào</dt>
                      <dd>{new Date(booking.startTime).toLocaleString("vi-VN")}</dd>
                    </div>
                    <div>
                      <dt>Dự kiến ra</dt>
                      <dd>
                        {booking.endTime
                          ? new Date(booking.endTime).toLocaleString("vi-VN")
                          : "Chưa xác định"}
                      </dd>
                    </div>
                    <div>
                      <dt>Đặt lúc</dt>
                      <dd>{new Date(booking.createdAt).toLocaleString("vi-VN")}</dd>
                    </div>
                  </dl>
                  {booking.status === "PENDING" && (
                    <button
                      type="button"
                      className="btn btn--outline"
                      onClick={() => cancelBooking(booking.id)}
                      disabled={cancellingBookingId !== null}
                    >
                      {cancellingBookingId === booking.id
                        ? "Đang hủy..."
                        : "Hủy chỗ"}
                    </button>
                  )}
                </li>
              ))}
            </ul>
          )}
        </section>
      )}

      <div className="customer-message customer-message--notice">
        <strong>Tình trạng bãi tại thời điểm cập nhật</strong>
        <span>
          Số chỗ có thể nhận xe đã trừ các booking đang giữ và xe đang ở bãi.
          Booking chưa giữ một ô vật lý cụ thể; trạng thái từng ô chỉ đổi khi
          nhân viên xác nhận xe đã đỗ thực tế.
        </span>
      </div>
    </section>
  );
}

export default function CustomerDashboard({ publicView = false }) {
  const auth = useSyncExternalStore(subscribeAuthSession, getAuthSession, getAuthSession);
  if (auth?.userProfile.role === "ADMIN" || auth?.userProfile.role === "MANAGER") return <ParkingSlotManagement role={auth.userProfile.role} />;
  if (publicView) return <DashboardContent publicView />;
  return (
    <RequireCustomer>
      <DashboardContent />
    </RequireCustomer>
  );
}
