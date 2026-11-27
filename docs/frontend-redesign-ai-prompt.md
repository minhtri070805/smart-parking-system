# Prompt redesign toàn bộ Frontend Smart Parking

Bạn là Senior Product Designer kiêm Senior Frontend Engineer. Hãy trực tiếp redesign và refactor toàn bộ frontend của project Smart Parking trong repository hiện tại. Không chỉ đưa ra mockup hoặc kế hoạch: sau khi audit, hãy sửa code thật, chạy kiểm tra và bàn giao một frontend hoàn chỉnh, nhất quán, responsive, dễ dùng trong vận hành thực tế.

## Bắt buộc đọc trước khi sửa

1. Đọc đầy đủ `/Users/tim/.agents/skills/design-taste-frontend/SKILL.md` và các reference mà skill yêu cầu cho software/web app.
2. Audit `frontend/smart-parking`, đặc biệt `App.jsx`, `main.jsx`, `Header.jsx`, `WorkspaceUI.jsx`, `services/navigation.js`, API/auth services, toàn bộ page, component Staff, CSS và test.
3. Đọc tài liệu nghiệp vụ trong `docs/product`, `docs/api`, `docs/testing`; không tự bịa endpoint, quyền hạn, số liệu hoặc trạng thái.

## Bối cảnh phải giữ

- React 18, Vite 5, React Router 6, CSS thuần, Leaflet và Three.js.
- Router basename `/app`; production build xuất vào `backend/src/SmartParking.Api/wwwroot/app`.
- Bốn vai trò: `ADMIN`, `MANAGER`, `STAFF`, `USER`.
- Giữ API contract, auth/session/refresh-token, role guard, query parameter, deep link, dữ liệu thật, error handling, polling, snapshot và stale guard.
- Không chỉnh backend hoặc thay Leaflet/Three.js chỉ để thuận tiện cho giao diện.
- Không xóa test, bỏ accessibility assertion hoặc hard-code dữ liệu demo vào production.

## Phạm vi redesign

- Public/auth: `/`, `/login`, `/register`, Google login, `/thong-tin/:topic` và toàn bộ trạng thái guest/loading/error/session expiry.
- Customer: chọn bãi/tầng, map, 3D, đặt chỗ, cọc/thanh toán, phiên gửi xe, vé tháng, hồ sơ và xe.
- Staff: xe vãng lai, booking online, xe ra, chụp ảnh biển số, xác minh, điều phối ô và ca làm.
- Manager: tổng quan bãi, phiên xe, ô đỗ, doanh thu, báo cáo, đối soát và ca làm.
- Admin: dashboard, bãi/tầng/khu/ô/chính sách/vé, tài khoản/phân công, audit log và trợ lý quản trị.
- Shared: 3D, map, notification, profile, modal, empty/loading/error/success/stale/offline và 404.

## Mục tiêu UX/UI

- Một design system cho toàn app: màu, type, spacing, radius, elevation, icon, motion, focus và breakpoint.
- Operations-first: nền neutral, chữ tương phản cao, xanh dương cho hành động, hổ phách cho cảnh báo. Không violet/lavender, neon, glassmorphism, gradient tràn lan hoặc shadow nặng.
- Icon SVG nhất quán; không dùng emoji làm icon nghiệp vụ.
- Không tạo card soup; dùng hierarchy, khoảng trắng và divider trước khi thêm container.
- Desktop workspace có sidebar/header rõ; mobile dùng navigation phù hợp, không chỉ thu nhỏ sidebar.
- Bảng dùng được trên mobile; form có label nhìn thấy, lỗi cạnh field, required/optional, loading và confirm đúng.
- Đủ loading, empty, retry, success, stale/offline và disabled state. Status luôn có chữ/icon, không chỉ màu.
- Motion ngắn, có mục đích và hỗ trợ `prefers-reduced-motion`; không scroll-jacking, parallax, carousel tự chạy hoặc fade-in mọi section.
- Nội dung tiếng Việt tự nhiên, thuật ngữ nghiệp vụ nhất quán, không phát minh marketing claim.
- WCAG 2.1 AA: semantic HTML, keyboard, focus, modal focus management, Escape, aria, target 44px và contrast.
- Kiểm tra 1440x900, 1024x768, 768x1024, 390x844 và 320px.

## Kiến trúc và cách triển khai

1. Lập inventory route → vai trò → mục tiêu → API → state.
2. Tạo token/primitives/app shell trước; sau đó migrate public/auth, Customer, Staff, Manager/Admin, cuối cùng 3D/map.
3. Chuẩn hóa Button, IconButton, field, Badge, Alert, Skeleton, EmptyState, Modal, Tabs, table/list, Pagination và PageHeader.
4. Giảm CSS cascade; không chồng thêm override vô hạn. Xác định presentation layer canonical và dọn rule trùng sau khi xác minh.
5. Tách component quá lớn theo trách nhiệm mà không đổi payload hoặc thứ tự nghiệp vụ.
6. Lazy-load route nặng; giữ nguyên snapshot/reconnect/stale guard.
7. Làm theo phase nhỏ, chạy test liên quan và kiểm tra browser sau mỗi phase. Không dừng ở mức đề xuất.

## Quality gate

- Xác nhận baseline bằng `npm run build` và `npm test`; không che giấu lỗi có sẵn và không làm tăng lỗi.
- Không xóa test để có màu xanh. Cập nhật test khi UI contract hợp lý thay đổi; sửa code nếu hành vi hoặc accessibility hỏng.
- Đích bàn giao: build pass, test liên quan pass, không console error, broken route/link, overflow hoặc collision.
- Báo bundle size, accessibility/responsive QA, giới hạn chưa kiểm tra và danh sách file thay đổi.
- Nếu skill gate chạy được, báo nguyên văn `GATE:` và mọi waiver/open FAIL.

Hãy bắt đầu bằng audit và kế hoạch phase ngắn, sau đó triển khai ngay phase nền tảng. Không dừng ở mockup hoặc kế hoạch.
