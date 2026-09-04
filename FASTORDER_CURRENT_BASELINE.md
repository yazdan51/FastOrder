# مستند Baseline فعلی پروژه FastOrder

**تاریخ:** 2026-09-04  
**شاخه فعال:** `feature/scheduled-split-orders-1s`  
**وضعیت Git در زمان ثبت:** Working Tree تمیز و شاخه محلی با `origin` همگام  
**Checkpoint اصلی Feature:** `5461a01` — `Add scheduled BUY/SELL click workflow`

## 1. معماری فعلی
مسیر جدید FastOrder اطلاعات سفارش را از فرم رسمی کارگزاری نمی‌خواند. کاربر نماد، قیمت، حجم و سمت سفارش را در رابط رسمی کارگزاری تنظیم می‌کند و FastOrder فقط سمت، تعداد کلیک، زمان شروع و اسلات‌های یک‌ثانیه‌ای را مدیریت می‌کند.

هر کلیک همان سفارش کامل موجود در فرم رسمی کارگزاری را ارسال می‌کند و FastOrder هیچ تقسیم حجمی انجام نمی‌دهد.

## 2. ScheduledClickSession
اطلاعات اصلی نشست:
- `Broker`
- `Side`
- `TotalClickCount`
- `StartTime`
- `ClickedCount`
- `RemainingClickCount`

سمت سفارش با `ScheduledClickSide.Buy` و `ScheduledClickSide.Sell` مدیریت می‌شود و به `Order.Side` قدیمی وابسته نیست.

## 3. EasyTrader
BUY:
`[data-cy="oms-order-form-submit-button-buy"]`

Fallback:
`ارسال خرید`

SELL:
`[data-cy="oms-order-form-submit-button-sell"]`

Fallback:
`ارسال فروش`

سمت مخالف هرگز fallback نیست.

تست‌های BUY تک‌کلیک، BUY دوکلیک، بازیابی مجدد دکمه بعد از rerender و SELL موفق بوده‌اند.

## 4. Pishro Kaman
پیشرو می‌تواند BUY و SELL را هم‌زمان Render کند و فعال‌بودن سمت اهمیت دارد. منطق نهایی بر اساس سمت انتخاب‌شده عمل می‌کند و محتویات فرم سمت مقابل نادیده گرفته می‌شود.

تست نهایی:
- Pishro BUY: موفق
- Pishro SELL: موفق

## 5. Exchange Clock و Scheduler
مرجع زمان: `TSETMC Exchange Clock`

- `SynchronizeAsync`: Anchor اولیه
- `ValidateAsync`: Validation دوره‌ای بدون Re-anchor

رفتار Scheduler:
- اسلات یک‌ثانیه‌ای
- بدون burst catch-up
- fail-closed در stale clock
- `CLICKED` بدون auto-retry

## 6. Official UI Dispatcher
عملیات DOM رسمی از مسیر `OfficialOrderUiDispatcher` انجام می‌شود. در هر اسلات، Action سمت انتخاب‌شده دوباره از DOM فعلی پیدا می‌شود و DOM reference بین اسلات‌ها Cache نمی‌شود.

## 7. Multi-instance
نمونه:
`FastOrder.exe --instance 1`

هر Instance دارای WebView2 profile مستقل در مسیر:
`%LOCALAPPDATA%\FastOrder\WebView2\Instance-{id}`

`FastOrder.Manager` برای مدیریت چند Instance وجود دارد.

## 8. مرز امنیتی
اصول ثابت:
- عدم ارسال مستقیم Broker API
- عدم ساخت `POST` مستقیم توسط FastOrder
- عدم دسترسی به Token
- عدم دسترسی به Cookie
- عدم دسترسی به Credential
- عدم دسترسی به Browser Storage
- عدم خواندن Request Body

ارسال فقط از طریق رابط رسمی قابل‌مشاهده کارگزاری و کلیک رسمی انجام می‌شود.

`HTTP 200` اثبات نهایی معامله نیست؛ مرجع نهایی لیست رسمی سفارش‌های کارگزاری است.

## 9. Git و Repository
Branch:
`feature/scheduled-split-orders-1s`

Checkpoint اصلی Feature:
`5461a01 Add scheduled BUY/SELL click workflow`

فایل‌های موقت Prompt/Backup/Patch پاک شدند. `commit_push.cmd` در `.gitignore` قرار گرفت.

آخرین وضعیت بررسی‌شده:
`## feature/scheduled-split-orders-1s...origin/feature/scheduled-split-orders-1s`

## 10. مبنای ادامه توسعه
این سند Baseline ادامه پروژه است.

مرحله بعدی پیشنهادی:
1. پاک‌سازی کدهای قدیمی Read/Prepare/Quantity-based
2. ساده‌سازی نهایی UI
3. حفظ مسیر تست‌شده BUY/SELL
4. حفظ Scheduler، ExchangeClock، Multi-instance و Security Boundary
5. تغییرات مرحله‌ای با Commitهای کوچک و قابل بازگشت
