export const normalizePlateNumber = (value = "") =>
  value.trim().toUpperCase().replace(/\s+/g, " ");

export const formatVietnameseCarPlateInput = (value = "") => {
  const compact = value.toUpperCase().replace(/[^A-Z0-9]/g, "");
  const match = compact.match(/^(\d{0,2})([A-Z]?)(\d{0,5})/);
  if (!match) return "";
  const [, locality, series, serial] = match;
  let formatted = locality + series;
  if (series && serial) {
    formatted += `-${serial.slice(0, 3)}`;
    if (serial.length > 3) formatted += `.${serial.slice(3)}`;
  }
  return formatted;
};

export const isVietnameseCarPlate = (value = "") =>
  /^\d{2}[A-Z]\d{5}$/.test(normalizePlateNumber(value).replace(/[ .-]/g, ""));

export function validateVietnameseCarPlate(value) {
  const plate = normalizePlateNumber(value);
  if (!isVietnameseCarPlate(plate)) {
    throw new Error("Biển số ô tô cần theo dạng 51F-123.45 (2 số tỉnh, 1 chữ cái, 5 số).");
  }
  return plate;
}
