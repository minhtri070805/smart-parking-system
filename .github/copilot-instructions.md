# Smart Parking project instructions

## Project context
- This repository implements a Smart Parking system with a React/Vite web app in `frontend/smart-parking`, an ASP.NET Core backend, and supporting parking operations and payment services.
- Keep user-facing text in Vietnamese and make web interfaces usable on mobile devices.
- Keep long or reusable text, such as terms and policies, in dedicated constants/data files rather than embedding it in page components.

## Parking domain rules
- Booking status lifecycle:
  1. `PENDING_ARRIVAL`: a customer creates a web booking; the system holds the reservation for at most 30 minutes from `bookingTime`. If the vehicle has not checked in at the gate by `expireTime`, automatically transition the booking to `CANCELLED`.
  2. `CHECKED_IN_GATE`: gate staff verify the arriving vehicle and booking, then hand the booking off to coordination staff.
  3. `PARKED`: coordination staff capture/upload a real license-plate photo, record the parking slot and vehicle/customer notes, and save the completed parking record.
  4. `CANCELLED`: terminal status for bookings that expire before gate check-in. Expired bookings cannot be checked in.
- A successful booking holds its parking space for at most 30 minutes. If the driver has not checked in by then, the booking is automatically cancelled.
- Supported payment methods are bank QR code, VNPay e-wallet, and cash at the exit lane.
- A license plate may be linked to only one primary account.

## Implementation conventions
- Follow the existing structure, libraries, and naming patterns in the relevant app; do not introduce a second implementation or unnecessary dependencies.
- Keep components accessible, responsive, and consistent with existing styles.
- Do not commit credentials, secrets, or real personal data.
