@echo off
setlocal EnableExtensions
chcp 65001 >nul
title VALORANT Custom Resolution
set "CR_SELF=%~f0"
set "CR_ARG=%*"

rem ---- admin rights ----
fltmc >nul 2>&1
if errorlevel 1 (
    echo Requesting administrator rights...
    if defined CR_ARG (
        powershell -NoProfile -Command "Start-Process -FilePath $env:CR_SELF -ArgumentList $env:CR_ARG -Verb RunAs"
    ) else (
        powershell -NoProfile -Command "Start-Process -FilePath $env:CR_SELF -Verb RunAs"
    )
    exit /b
)

rem ---- run the PowerShell part stored below this line ----
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -Command "$f=[IO.File]::ReadAllText($env:CR_SELF,[Text.Encoding]::UTF8); $i=$f.IndexOf('##PS'+'START##'); Invoke-Expression $f.Substring($i)"
echo.
pause
exit /b
##PSSTART##
# =====================================================================
#  NVIDIA Custom Resolution - console version
#  Only the PRIMARY display is changed; the picture is stretched to the full screen right away
#  (GPU scaling), the other monitors keep their resolution, refresh rate and position.
#
#  custom_res.bat            interactive (asks for the language)
#  custom_res.bat /en        English
#  custom_res.bat /ru        Russian (Русский)
#  custom_res.bat /restore   only re-enable the monitors disabled by a previous run
#  custom_res.bat /nodisable do not disable the monitors (for troubleshooting)
#  custom_res.bat /nocolors  do not save/restore brightness, contrast, gamma, vibrance, hue (for troubleshooting)
#  custom_res.bat /force     do not refuse a refresh rate above what the monitor lists (can distort the picture!)
#  custom_res.bat /primaryonly disable only the monitor device of the primary display (default: the monitor devices of all displays)
#  custom_res.bat /nostretch do not stretch (apply the mode 1:1, the monitor decides what to do with it)
#
#  The same engine is used by the GUI (CustomRes.exe, see CustomResGui.cs / build.bat).
# =====================================================================
$ErrorActionPreference = 'Stop'
$TestSeconds = 15      # seconds to wait for confirmation before the automatic rollback
$Stretch     = $true   # stretch to the full screen right away; /nostretch turns it off

# ---------------------------------------------------------------------
#  Shared engine (C# 5): translations, CVT timing, NVAPI, display configuration, the whole flow
# ---------------------------------------------------------------------
$core = @'
// CustomResCore.cs - shared core of "NVIDIA Custom Resolution" (used by the GUI and by custom_res.bat).
// C# 5 only, so that it compiles with the compiler built into Windows (csc.exe of .NET Framework 4)
// and with Add-Type in Windows PowerShell 5.1.
using System;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace CustomRes
{
    // =====================================================================
    //  Translations (add a language by adding one more column in A(...))
    // =====================================================================
    public static class Strings
    {
        public static string Lang = "en";
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> Ru = new Dictionary<string, string>();

        private static void A(string key, string en, string ru) { En[key] = en; Ru[key] = ru; }

        public static string T(string key, params object[] args)
        {
            Dictionary<string, string> d = (Lang == "ru") ? Ru : En;
            string s;
            if (!d.TryGetValue(key, out s) && !En.TryGetValue(key, out s)) return key;
            if (args != null && args.Length > 0)
            {
                try { return string.Format(s, args); } catch (FormatException) { return s; }
            }
            return s;
        }

        public static IEnumerable<string> Keys { get { return En.Keys; } }
        public static string Raw(string lang, string key)
        {
            Dictionary<string, string> d = (lang == "ru") ? Ru : En;
            string s;
            return d.TryGetValue(key, out s) ? s : null;
        }

        static Strings()
        {
            A("lbl_error", "ERROR", "ОШИБКА");
            A("lbl_note", "Note", "Внимание");
            A("lbl_warning", "WARNING", "ПРЕДУПРЕЖДЕНИЕ");

            A("width_range", "Width must be between 320 and 8192.", "Ширина должна быть от 320 до 8192.");
            A("height_range", "Height must be between 200 and 8192.", "Высота должна быть от 200 до 8192.");
            A("hz_range", "Refresh rate must be between 24 and 1000 Hz.", "Частота должна быть от 24 до 1000 Гц.");
            A("caveat_w8",
              "Width {0} is not a multiple of 8. The CVT standard assumes multiples of 8, so the driver or monitor may refuse the mode, show artifacts (shifted picture, stripes) or a black screen. The timing is adjusted to a multiple of 8 by enlarging the back porch. The NVIDIA test and the automatic rollback still apply.",
              "Ширина {0} не кратна 8. Стандарт CVT рассчитан на кратность 8, поэтому драйвер или монитор могут отказаться от режима, показать артефакты (сдвиг картинки, полосы) или чёрный экран. Тайминг подгоняется под кратность 8 за счёт увеличения back porch. Тест NVIDIA и автоматический откат остаются.");
            A("err_hbp", "Invalid horizontal timing (back porch = {0}).", "Некорректный горизонтальный тайминг (back porch = {0}).");
            A("err_vbp", "Invalid vertical timing (back porch = {0}).", "Некорректный вертикальный тайминг (back porch = {0}).");
            A("err_total16", "HTotal/VTotal do not fit into 16 bits.", "HTotal/VTotal не помещаются в 16 бит.");
            A("err_hz", "The timing gives {0:N3} Hz instead of {1} Hz.", "Получилась частота {0:N3} Гц вместо {1} Гц.");
            A("warn_pclk", "Very high pixel clock ({0:N0} MHz): the cable or port may not have enough bandwidth.", "Очень высокий пиксельный клок ({0:N0} МГц): кабелю или порту может не хватить пропускной способности.");

            A("no_monitors", "  No enabled monitors found in Device Manager (they may already be disabled).", "  Включённых мониторов в диспетчере устройств не найдено (возможно, уже отключены).");
            A("mon_disabled", "  disabled: {0}   [{1}]", "  отключён: {0}   [{1}]");
            A("mon_disable_fail", "  could not disable {0}: {1}", "  не удалось отключить {0}: {1}");
            A("mon_enabled", "  enabled: {0}", "  включён: {0}");
            A("mon_enable_fail", "  could not enable {0}: {1}", "  не удалось включить {0}: {1}");
            A("restoring", "Re-enabling monitors...", "Включаю мониторы обратно...");
            A("stale_state", "Found monitors disabled by a previous run - enabling them back...", "Нашёл мониторы, отключённые прошлым запуском - включаю обратно...");

            A("need64", "A 64-bit process is required (nvapi64.dll).", "Нужен 64-битный процесс (nvapi64.dll).");
            A("no_disp", "No active displays found.", "Активные дисплеи не найдены.");
            A("primary_not_nvidia", "The primary display is not connected to an NVIDIA GPU.", "Основной дисплей подключён не к видеокарте NVIDIA.");
            A("primary_info", "Primary display: {0}  ({1}), now {2}x{3}@{4}", "Основной дисплей: {0}  ({1}), сейчас {2}x{3}@{4}");
            A("only_primary", "Only the primary display is changed. The other monitors keep their resolution, refresh rate and position.", "Меняется только основной дисплей. Остальные мониторы сохраняют разрешение, частоту и расположение.");

            A("step_disable", "Disabling monitors in Device Manager (driver only)...", "Отключаю мониторы в диспетчере устройств (только драйвер)...");
            A("step_create", "Creating the resolution in the NVIDIA driver...", "Создаю разрешение в драйвере NVIDIA...");
            A("step_stretch", "Stretching to the full screen (GPU scaling) and keeping the layout of the other monitors...", "Растягиваю на весь экран (масштабирование на GPU) и сохраняю расположение остальных мониторов...");
            A("step_check", "Checking the result...", "Проверяю результат...");
            A("step_save", "Saving and finishing...", "Сохраняю и завершаю...");

            A("att_gettiming_fail", "  {0}: the driver could not calculate the timing: {1} (code {2})", "  {0}: драйвер не смог рассчитать тайминг: {1} (код {2})");
            A("att_mismatch", "  {0}: the driver returned {1}x{2} @ {3:N2} Hz instead of the requested mode, skipping", "  {0}: драйвер вернул {1}x{2} @ {3:N2} Гц вместо запрошенного режима, пропускаю");
            A("att_try_fail", "  {0}: NVIDIA rejected it: {1} (code {2})", "  {0}: NVIDIA отклонила: {1} (код {2})");
            A("att_ok", "  Using the {0} timing from the driver: H {1}/{2}  V {3}/{4}, pixel clock {5:N2} MHz, {6:N3} Hz", "  Использую тайминг {0} от драйвера: H {1}/{2}  V {3}/{4}, пиксельный клок {5:N2} МГц, {6:N3} Гц");
            A("att_scaled", "  Using the driver's {0} timing: the monitor keeps its native {1}x{2} @ {3:N2} Hz and the GPU stretches {4}x{5} to it (the same as NVIDIA Control Panel does with the \"Automatic\" timing).",
                            "  Использую тайминг {0} от драйвера: монитор остаётся на родном {1}x{2} @ {3:N2} Гц, а видеокарта растягивает на него {4}x{5} (так же делает панель NVIDIA с таймингом \"Automatic\").");
            A("att_not_native", "  {0}: the driver returned {1}x{2} @ {3:N2} Hz, which is not the monitor's native timing, skipping", "  {0}: драйвер вернул {1}x{2} @ {3:N2} Гц, это не родной тайминг монитора, пропускаю");
            A("sum_stretch", "  Output     : the monitor keeps its native timing, the GPU stretches {0}x{1} to the full screen", "  Вывод      : монитор остаётся на родном тайминге, видеокарта растягивает {0}x{1} на весь экран");
            A("signal_native", "  Signal to the monitor: {0}x{1} - the GPU does the scaling, so the monitor adds no bars.", "  Сигнал на монитор: {0}x{1} - масштабирует видеокарта, монитор полос не добавляет.");
            A("signal_1to1", "  Signal to the monitor: {0}x{1} (1:1) - the monitor does the scaling and may add bars. Check the monitor's own aspect/scaling setting and NVIDIA Control Panel (Perform scaling on: GPU).",
                             "  Сигнал на монитор: {0}x{1} (1:1) - масштабирует монитор и может добавить полосы. Проверьте параметр формата/масштабирования в меню монитора и в панели NVIDIA (Масштабирование выполняется на: GPU).");
            A("att_manual", "  Driver-calculated timings did not work, trying the built-in CVT-RB timing...", "  Тайминги от драйвера не подошли, пробую встроенный CVT-RB...");
            A("all_rejected", "NVIDIA rejected every timing, last error: {0} (code {1})", "NVIDIA отклонила все тайминги, последняя ошибка: {0} (код {1})");
            A("accepted", "  NVIDIA accepted the timing (test passed).", "  NVIDIA приняла тайминг (тест пройден).");

            A("stretch_unavailable", "  Full-screen scaling through the NVIDIA driver is not available here; the mode will be applied 1:1 (the monitor may add bars).", "  Масштабирование на весь экран через драйвер NVIDIA здесь недоступно; режим будет применён 1:1 (монитор может добавить полосы).");
            A("stretch_cfg_fail", "  The NVIDIA display configuration was rejected ({0}, code {1}); the mode is applied 1:1 instead (the monitor may add bars).", "  Конфигурация дисплеев NVIDIA отклонена ({0}, код {1}); режим применён 1:1 (монитор может добавить полосы).");
            A("stretch_applied", "  Full-screen (GPU) scaling applied: source {0}x{1}.", "  Масштабирование на весь экран (GPU) применено: источник {0}x{1}.");
            A("stretch_state", "  Driver reports: source {0}x{1}, scaling: {2}", "  Драйвер сообщает: источник {0}x{1}, масштабирование: {2}");
            A("stretch_inactive", "  Warning: the driver does not report full-screen scaling (scaling: {0}).", "  Внимание: драйвер не сообщает о масштабировании на весь экран (масштабирование: {0}).");
            A("sc_1", "full-screen (balanced)", "на весь экран (баланс)");
            A("sc_2", "full-screen (GPU)", "на весь экран (GPU)");
            A("sc_3", "no scaling / centered (GPU)", "без масштабирования, по центру (GPU)");
            A("sc_5", "aspect ratio (GPU)", "с сохранением пропорций (GPU)");
            A("sc_6", "aspect ratio (balanced)", "с сохранением пропорций (баланс)");
            A("sc_7", "no scaling / centered (balanced)", "без масштабирования, по центру (баланс)");
            A("sc_8", "integer scaling (GPU)", "целочисленное масштабирование (GPU)");
            A("sc_other", "other ({0})", "другое ({0})");
            A("layout_fixed", "  Display layout restored: the other monitors are back where they were relative to the primary display.", "  Расположение мониторов восстановлено: остальные мониторы стоят там же относительно основного.");

            A("col_saved", "  Color settings saved ({0}); they are put back if the driver resets them.", "  Настройки цвета сохранены ({0}); если драйвер их сбросит, они будут возвращены.");
            A("col_none", "  Could not read the color settings, so they will not be restored.", "  Не удалось прочитать настройки цвета, поэтому восстанавливать их не буду.");
            A("col_restored", "  The driver reset the color settings, put back: {0}.", "  Драйвер сбросил настройки цвета, вернул: {0}.");
            A("col_ok", "  Color settings checked: nothing was reset.", "  Настройки цвета проверены: ничего не сбросилось.");
            A("col_fail", "  Could not put back: {0}. Set it again in NVIDIA Control Panel (Adjust desktop color settings).", "  Вернуть не удалось: {0}. Выставьте заново в панели NVIDIA (Регулировка параметров цвета рабочего стола).");
            A("col_gamma", "brightness/contrast/gamma", "яркость/контрастность/гамма");
            A("col_dvc", "digital vibrance", "насыщенность (Digital Vibrance)");
            A("col_hue", "hue", "оттенок");

            A("att_native", "  {0}: the driver has its own {1}x{2} @ {3:N2} Hz timing (the monitor's native one), so it is used and no custom resolution is created.",
                            "  {0}: у драйвера есть собственный тайминг {1}x{2} @ {3:N2} Гц (родной для монитора), он и используется, кастомное разрешение не создаётся.");
            A("env_info", "  Windows {0} (build {1}); displays attached: {2}.", "  Windows {0} (сборка {1}); подключено дисплеев: {2}.");
            A("mon_primary_only", "  Only the monitor of the primary display is disabled (/primaryonly); the monitors of the other displays stay enabled.",
                                  "  Отключается только монитор основного дисплея (/primaryonly); мониторы остальных дисплеев остаются включёнными.");
            A("mon_primary_unknown", "  Could not tell which monitor device belongs to {0}, so none was disabled.",
                                     "  Не удалось определить, какое устройство монитора относится к {0}, поэтому ни одно не отключено.");
            A("disp_lost", "  Windows switched a display off: {0} ({1}x{2}). Bringing it back...", "  Windows выключила дисплей: {0} ({1}x{2}). Возвращаю...");
            A("disp_back", "  The display is back on, at its old place.", "  Дисплей снова включён на прежнем месте.");
            A("disp_still_off", "  The display {0} is still off. Turn it on in Windows: Win+P > Extend, or Settings > System > Display.", "  Дисплей {0} всё ещё выключен. Включите его в Windows: Win+P > Расширить, или Параметры > Система > Дисплей.");
            A("disp_extended", "  Another display was switched off: the desktop is extended onto it again.", "  Другой дисплей был выключен: рабочий стол снова расширен на него.");
            A("gui_pv_native", "Your monitor: {0}×{1}", "Ваш монитор: {0}×{1}");
            A("gui_pv_stretch", "Stretched ×{0:0.00} wide, ×{1:0.00} tall", "Растянуто ×{0:0.00} по ширине, ×{1:0.00} по высоте");
            A("gui_pv_bars", "Not stretched: the monitor adds bars", "Без растягивания: монитор добавит полосы");
            A("gui_orig_tip", "Your original resolution, {0}x{1} @ {2} Hz. It is remembered between runs.",
                              "Ваше исходное разрешение, {0}x{1} @ {2} Гц. Запоминается между запусками.");
            A("stale_custom_removed", "  Removed a saved custom {0}x{1} @ {2:N0} Hz timing: it overrode the monitor's own timing for this resolution.",
                                      "  Удалено сохранённое кастомное разрешение {0}x{1} @ {2:N0} Гц: его собственный тайминг перекрывал родной тайминг монитора для этого разрешения.");
            A("done_native", "done: the monitor's native timing is applied (nothing custom was saved)", "готово: применён родной тайминг монитора (ничего кастомного не сохранялось)");
            A("hz_over_limit", "The monitor reports at most {0} Hz at its native {1}x{2}, but {3:N0} Hz was requested. A refresh rate above the monitor's limit gives a distorted picture and artifacts, so nothing was changed. Use {0} Hz or less.",
                               "Монитор сообщает максимум {0} Гц на родном {1}x{2}, а запрошено {3:N0} Гц. Частота выше предела монитора даёт искажённую картинку и артефакты, поэтому ничего не менялось. Укажите {0} Гц или меньше.");
            A("keep_disabled_warn", "  Warning: while the monitors are disabled the driver does not check modes against the monitor's limits. If you see artifacts or noise, restore the monitors.",
                                    "  Внимание: пока мониторы отключены, драйвер не сверяет режимы с пределами монитора. Если появились артефакты или помехи, верните мониторы.");
            A("gui_monitors_off", "The monitors are still disabled from a previous run. In this state the driver does not check modes against the monitor's limits, which can cause artifacts and noise. Press \"Restore monitors\".",
                                  "Мониторы всё ещё отключены с прошлого запуска. В таком состоянии драйвер не сверяет режимы с пределами монитора, из-за чего бывают артефакты и помехи. Нажмите «Вернуть мониторы».");

            A("mode_match", "  Active: {0}x{1}@{2} - matches the requested mode.", "  Активно: {0}x{1}@{2} - совпадает с запрошенным.");
            A("mode_diff", "  Windows reports {0}x{1}@{2}, expected {3}x{4}@{5:N0}.", "  Windows показывает {0}x{1}@{2}, ожидалось {3}x{4}@{5:N0}.");
            A("keep_question", "Does the picture look right?", "Картинка нормальная?");
            A("keep_prompt", "  Does the picture look right? Y = keep, N = revert. Auto-revert in {0} s   ", "  Картинка нормальная? Y = оставить, N = откатить. Авто-откат через {0} с   ");

            A("save_fail", "could not save: {0} (code {1})", "не удалось сохранить: {0} (код {1})");
            A("saved_listed", "  Saved: the resolution now appears in the NVIDIA custom resolution list.", "  Сохранено: разрешение появилось в списке пользовательских разрешений NVIDIA.");
            A("saved_notlisted", "  Saved, but the resolution was not found in the NVIDIA list - check NVIDIA Control Panel.", "  Сохранение прошло, но в списке NVIDIA разрешение не нашлось - проверьте в панели NVIDIA.");
            A("win_apply_fail", "created and saved, but Windows did not apply the mode (code {0})", "создано и сохранено, но Windows не применила режим (код {0})");
            A("done", "done: created, saved and applied", "готово: создано, сохранено и применено");
            A("done_stretch", "done: created, saved and stretched to the full screen", "готово: создано, сохранено и растянуто на весь экран");
            A("reverted", "reverted: not confirmed (or time ran out)", "откат: вы не подтвердили (или время вышло)");
            A("rollback_note", "  Rolled back: the original display configuration was restored.", "  Откат: исходная конфигурация дисплеев восстановлена.");

            A("reenable_prompt", "Re-enable the monitors in Device Manager? [Y/n]", "Включить мониторы обратно в диспетчере устройств? [Y/n]");
            A("keep_disabled", "Leaving them disabled. Restore later with: {0}", "Оставляю отключёнными. Вернуть позже: {0}");
            A("gui_restore_hint", "the \"Restore monitors\" button", "кнопка «Вернуть мониторы»");
            A("persist_ok", "  After re-enabling the monitors the mode persisted: {0}x{1}.", "  После включения мониторов режим сохранился: {0}x{1}.");
            A("persist_lost", "  The mode was reset after re-enabling the monitors, applying it again...", "  После включения мониторов режим сбросился, применяю снова...");
            A("err_generic", "error: {0}", "ошибка: {0}");
            A("fatal", "ERROR: {0}", "ОШИБКА: {0}");
            A("summary_hdr", "Summary:", "Итог:");

            // console prompts
            A("res_prompt", "Enter a resolution (e.g. 1280x960 or 1280x960@240; empty = quit)", "Введите разрешение (например 1280x960 или 1280x960@240; пусто = выход)");
            A("cancelled", "Cancelled.", "Отменено.");
            A("bad_format", "  Unrecognized format. Example: 1280x960 or 1280x960@240", "  Не понял формат. Пример: 1280x960 или 1280x960@240");
            A("hz_prompt", "Refresh rate, Hz [Enter = {0}]", "Частота обновления, Гц [Enter = {0}]");
            A("hz_bad", "  Refresh rate not recognized.", "  Частота не распознана.");
            A("sum_res", "  Resolution : {0}x{1} @ {2:N3} Hz", "  Разрешение : {0}x{1} @ {2:N3} Гц");
            A("sum_timing", "  Timing     : CVT-RB, H {0}/{1}  V {2}/{3}, pixel clock {4:N2} MHz, line rate {5:N1} kHz", "  Тайминг    : CVT-RB, H {0}/{1}  V {2}/{3}, пиксельный клок {4:N2} МГц, строчная {5:N1} кГц");
            A("timing_ok", "  Timing check passed.", "  Проверка тайминга пройдена.");
            A("confirm_caveat", "  Confirm that you want to continue with this resolution (Y = yes, Enter = enter another)", "  Подтвердите, что хотите продолжить с этим разрешением (Y = да, Enter = ввести другое)");
            A("go_prompt", "Create and test it in NVIDIA? [Y/n]", "Создать и протестировать в NVIDIA? [Y/n]");

            // GUI
            A("gui_title", "VALORANT Custom Resolution", "VALORANT Custom Resolution");
            A("gui_display", "Primary display", "Основной дисплей");
            A("gui_display_none", "No NVIDIA primary display found", "Основной дисплей NVIDIA не найден");
            A("gui_resolution", "Resolution", "Разрешение");
            A("gui_width", "Width", "Ширина");
            A("gui_height", "Height", "Высота");
            A("gui_hz", "Refresh rate, Hz", "Частота, Гц");
            A("gui_presets", "Presets", "Готовые");
            A("gui_preset_pick", "(pick a preset)", "(выберите готовое)");
            A("gui_options", "Options", "Параметры");
            A("gui_stretch", "Stretch to the full screen (GPU scaling)", "Растянуть на весь экран");
            A("gui_disable", "Disable the drivers of all monitors in Device Manager while creating", "Отключить драйверы всех мониторов в диспетчере устройств на время создания");
            A("gui_reenable", "Enable the monitors again afterwards", "Включить мониторы обратно после применения");
            A("gui_apply", "Create and apply", "Создать и применить");
            A("gui_restore", "Restore monitors", "Вернуть мониторы");
            A("gui_close", "Close", "Закрыть");
            A("gui_lang", "Language", "Язык");
            A("gui_log", "Log", "Журнал");
            A("gui_keep_title", "Keep this resolution?", "Оставить это разрешение?");
            A("gui_keep_text", "Does the picture look right?", "Картинка нормальная?");
            A("gui_keep", "Keep", "Оставить");
            A("gui_revert", "Revert", "Откатить");
            A("gui_revert_in", "Reverting automatically in {0} s", "Автоматический откат через {0} с");
            A("gui_invalid", "Please check the input", "Проверьте ввод");
            A("gui_confirm_title", "Please confirm", "Подтвердите");
            A("gui_reenable_q", "Enable the monitors again?", "Включить мониторы обратно?");
            A("gui_restore_done", "Monitors restored.", "Мониторы возвращены.");
            A("gui_busy", "Working...", "Работаю...");
            A("gui_finished", "Finished: {0}", "Завершено: {0}");
            A("gui_hint", "Only the primary display is changed. The other monitors keep their resolution, refresh rate and position.", "Меняется только основной дисплей. Остальные мониторы сохраняют разрешение, частоту и расположение.");
            A("gui_timing_stretch", "The monitor keeps its native timing; the GPU stretches {0}x{1} to the full screen.", "Монитор остаётся на родном тайминге; видеокарта растягивает {0}x{1} на весь экран.");
            A("gui_timing", "Timing: CVT-RB, H {0}/{1}  V {2}/{3}, {4:N2} MHz, {5:N3} Hz", "Тайминг: CVT-RB, H {0}/{1}  V {2}/{3}, {4:N2} МГц, {5:N3} Гц");
            A("gui_not_admin", "Run this program as administrator.", "Запустите программу от имени администратора.");
        }
    }

    // =====================================================================
    //  CVT Reduced Blanking timing and validation
    // =====================================================================
    public class Timing
    {
        public int W, H;
        public double Hz;
        public int HFP = 48, HSW = 32, HTotal, HPol = 0;
        public int VFP = 3, VSW, VTotal, VPol = 1;
        public uint Pclk10k;
        public double PclkMHz, HFreqKHz, RealHz;
    }

    public class Validation
    {
        public List<string> Errors = new List<string>();
        public List<string> Warnings = new List<string>();
        public List<string> Caveats = new List<string>();
        public Timing Timing;
    }

    public static class Cvt
    {
        public static Timing Calc(int w, int h, double hz)
        {
            double ar = (double)w / h;
            int vs = 10;
            int[][] table = new int[][] { new int[] { 4, 3, 4 }, new int[] { 16, 9, 5 }, new int[] { 16, 10, 6 }, new int[] { 5, 4, 7 }, new int[] { 15, 9, 7 } };
            foreach (int[] p in table)
            {
                if (Math.Abs(ar - ((double)p[0] / p[1])) < 0.005) { vs = p[2]; break; }
            }
            double hPeriodUs = ((1.0 / hz) - 0.000460) / h * 1e6;
            int vbi = (int)Math.Floor(460.0 / hPeriodUs) + 1;
            int minVbi = 3 + vs + 6;
            if (vbi < minVbi) vbi = minVbi;
            int hTotal = (int)(Math.Ceiling((w + 160) / 8.0) * 8);   // multiple of 8; extra pixels go into the back porch
            int vTotal = h + vbi;
            uint pclk = (uint)Math.Ceiling(hTotal * (double)vTotal * hz / 10000.0);

            Timing t = new Timing();
            t.W = w; t.H = h; t.Hz = hz;
            t.HTotal = hTotal; t.VSW = vs; t.VTotal = vTotal;
            t.Pclk10k = pclk;
            t.PclkMHz = pclk / 100.0;
            t.HFreqKHz = (pclk * 10.0) / hTotal;
            t.RealHz = (pclk * 10000.0) / ((double)hTotal * vTotal);
            return t;
        }

        public static Validation Validate(int w, int h, double hz) { return Validate(w, h, hz, false); }

        // gpuScaled: the monitor keeps its native timing and the GPU stretches the picture, so only the ranges matter
        // (the built-in timing is just a fallback)
        public static Validation Validate(int w, int h, double hz, bool gpuScaled)
        {
            Validation v = new Validation();
            if (w < 320 || w > 8192) v.Errors.Add(Strings.T("width_range"));
            if (h < 200 || h > 8192) v.Errors.Add(Strings.T("height_range"));
            if (hz < 24 || hz > 1000) v.Errors.Add(Strings.T("hz_range"));
            if (v.Errors.Count > 0) return v;

            Timing t = Calc(w, h, hz);
            v.Timing = t;
            if (gpuScaled) return v;
            int hbp = t.HTotal - t.W - t.HFP - t.HSW;
            int vbp = t.VTotal - t.H - t.VFP - t.VSW;
            if (hbp <= 0) v.Errors.Add(Strings.T("err_hbp", hbp));
            if (vbp <= 0) v.Errors.Add(Strings.T("err_vbp", vbp));
            if (t.HTotal > 65535 || t.VTotal > 65535) v.Errors.Add(Strings.T("err_total16"));
            if (Math.Abs(t.RealHz - t.Hz) / t.Hz > 0.005) v.Errors.Add(Strings.T("err_hz", t.RealHz, t.Hz));
            if (t.PclkMHz > 600) v.Warnings.Add(Strings.T("warn_pclk", t.PclkMHz));
            if (w % 8 != 0) v.Caveats.Add(Strings.T("caveat_w8", w));
            return v;
        }

        private static readonly Regex Rx = new Regex(
            @"^\s*(\d{3,5})\s*[xX\u0445\u0425\u00D7*]\s*(\d{3,5})(?:\s*@\s*(\d{2,4}(?:[.,]\d+)?))?\s*$");

        // "1280x960", "1280x960@240", Cyrillic x and * are accepted; hz = 0 when it was not given
        public static bool TryParse(string input, out int w, out int h, out double hz)
        {
            w = 0; h = 0; hz = 0;
            if (input == null) return false;
            Match m = Rx.Match(input);
            if (!m.Success) return false;
            w = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            h = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (m.Groups[3].Success && m.Groups[3].Length > 0) hz = ParseHz(m.Groups[3].Value);
            return true;
        }

        public static bool TryParseHz(string text, out double hz)
        {
            hz = 0;
            if (string.IsNullOrEmpty(text)) return false;
            return double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out hz);
        }

        private static double ParseHz(string s)
        {
            return double.Parse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }

    // =====================================================================
    //  NVAPI (nvapi64.dll, functions are looked up through nvapi_QueryInterface)
    // =====================================================================
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NvSourceMode
    {
        public uint width; public uint height; public uint colorDepth;
        public uint colorFormat;
        public int posX; public int posY;
        public uint spanning;
        public uint bits;          // bit 0 = GDI primary, bit 1 = SLI focus
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NvAdv
    {
        public uint version; public uint rotation; public uint scaling; public uint refreshRate1K;
        public uint bits; public uint connector; public uint tvFormat; public uint timingOverride;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 96)] public byte[] timing;
    }

    public class NvTarget
    {
        public uint DisplayId; public uint TargetId; public NvAdv Adv;
    }

    public class NvPath
    {
        public uint SourceId; public uint Bits; public bool NonNvidia;
        public byte[] Luid = new byte[8];
        public NvSourceMode Src;
        public List<NvTarget> Targets = new List<NvTarget>();
        public bool IsGdiPrimary { get { return (Src.bits & 1) != 0; } }
    }

    public class NvConfig
    {
        public List<NvPath> Paths = new List<NvPath>();

        public NvConfig Clone()
        {
            NvConfig c = new NvConfig();
            foreach (NvPath p in Paths)
            {
                NvPath q = new NvPath();
                q.SourceId = p.SourceId; q.Bits = p.Bits; q.NonNvidia = p.NonNvidia;
                q.Luid = (byte[])p.Luid.Clone();
                q.Src = p.Src;
                foreach (NvTarget t in p.Targets)
                {
                    NvTarget u = new NvTarget();
                    u.DisplayId = t.DisplayId; u.TargetId = t.TargetId; u.Adv = t.Adv;
                    u.Adv.timing = (t.Adv.timing != null) ? (byte[])t.Adv.timing.Clone() : new byte[96];
                    q.Targets.Add(u);
                }
                c.Paths.Add(q);
            }
            return c;
        }
    }

    public static class NvApi
    {
        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr QueryInterface(uint id);

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        public struct ViewPortF { public float x; public float y; public float w; public float h; }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        public struct TimingExt
        {
            public uint flag; public ushort rr; public uint rrx1k; public uint aspect;
            public ushort rep; public uint status;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 40)] public byte[] name;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        public struct NvTiming
        {
            public ushort HVisible; public ushort HBorder; public ushort HFrontPorch; public ushort HSyncWidth; public ushort HTotal;
            public byte HSyncPol;
            public ushort VVisible; public ushort VBorder; public ushort VFrontPorch; public ushort VSyncWidth; public ushort VTotal;
            public byte VSyncPol;
            public ushort interlaced;
            public uint pclk;
            public TimingExt etc;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        public struct CustomDisplay
        {
            public uint version; public uint width; public uint height; public uint depth; public uint colorFormat;
            public ViewPortF srcPartition;
            public float xRatio; public float yRatio;
            public NvTiming timing;
            public uint flags;
        }

        // NV_TIMING_INPUT: NV_TIMING_FLAG is 12 bytes (bit-fields, union, scaling), so it is three uints here
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        public struct TimingInput
        {
            public uint version; public uint width; public uint height; public float rr;
            public uint flag0; public uint flag1; public uint flag2;
            public uint type;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        internal struct NvPathInfoN
        {
            public uint version; public uint sourceId; public uint targetInfoCount;
            public IntPtr targetInfo; public IntPtr sourceModeInfo;
            public uint bits;
            public IntPtr osAdapterId;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        internal struct NvPathTargetN { public uint displayId; public IntPtr details; public uint targetId; }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DInit();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private delegate int DGetId([MarshalAs(UnmanagedType.LPStr)] string name, out uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DGetTiming(uint id, ref TimingInput ti, ref NvTiming t);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DTry(uint[] ids, uint count, ref CustomDisplay cd);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DRevert(uint[] ids, uint count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DSave(uint[] ids, uint count, uint outputOnly, uint monitorOnly);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DDelete(uint[] ids, uint count, ref CustomDisplay cd);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DEnum(uint id, uint index, ref CustomDisplay cd);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private delegate int DErr(int status, StringBuilder sb);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DGetCfg(ref uint count, IntPtr paths);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DSetCfg(uint count, IntPtr paths, uint flags);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private delegate int DGetHandle([MarshalAs(UnmanagedType.LPStr)] string name, out IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DGetOutId(IntPtr handle, out uint outputId);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DInfoCall(IntPtr handle, uint outputId, IntPtr info);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DLevelCall(IntPtr handle, uint outputId, int value);

        // function IDs for nvapi_QueryInterface
        private const uint ID_Initialize = 0x0150E828;
        private const uint ID_GetErrorMessage = 0x6C2D048C;
        private const uint ID_GetDisplayIdByDisplayName = 0xAE457190;
        private const uint ID_GetTiming = 0x175167E9;
        private const uint ID_TryCustomDisplay = 0x1F7DB630;
        private const uint ID_RevertCustomDisplayTrial = 0xCBBD40F0;
        private const uint ID_SaveCustomDisplay = 0x49882876;
        private const uint ID_DeleteCustomDisplay = 0x552E5B9B;
        private const uint ID_EnumCustomDisplay = 0xA2072D59;
        private const uint ID_GetDisplayConfig = 0x11ABCCF8;
        private const uint ID_SetDisplayConfig = 0x5D8CF8DE;
        // color settings of the desktop (digital vibrance and hue; brightness/contrast/gamma live in the gamma ramp)
        private const uint ID_GetAssociatedDisplayHandle = 0x35C29134;
        private const uint ID_GetAssociatedOutputId = 0xD995937E;
        private const uint ID_GetDVCInfo = 0x4085DE45;
        private const uint ID_SetDVCLevel = 0x172409B4;
        private const uint ID_GetDVCInfoEx = 0x0E45002D;
        private const uint ID_SetDVCLevelEx = 0x4A82C2B1;
        private const uint ID_GetHUEInfo = 0x95B64341;
        private const uint ID_SetHUEAngle = 0xF5A0F22C;

        private static CustomDisplay pending;

        private static T Get<T>(uint id) where T : class
        {
            IntPtr p = QueryInterface(id);
            if (p == IntPtr.Zero) throw new Exception("NVAPI: function 0x" + id.ToString("X8") + " is not available (driver too old or too new?)");
            return (T)(object)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
        }

        private static CustomDisplay Blank()
        {
            CustomDisplay cd = new CustomDisplay();
            cd.version = (uint)(Marshal.SizeOf(typeof(CustomDisplay)) | (1 << 16));
            cd.timing.etc.name = new byte[40];
            return cd;
        }

        public static string Err(int status)
        {
            try
            {
                StringBuilder sb = new StringBuilder(64);
                Get<DErr>(ID_GetErrorMessage)(status, sb);
                return sb.ToString();
            }
            catch (Exception) { return "status " + status; }
        }

        public static void Init()
        {
            int s = Get<DInit>(ID_Initialize)();
            if (s != 0) throw new Exception("NvAPI_Initialize: " + Err(s) + " (" + s + ")");
        }

        public static uint DisplayId(string gdiName)
        {
            uint id;
            int s = Get<DGetId>(ID_GetDisplayIdByDisplayName)(gdiName, out id);
            if (s != 0) throw new Exception("NvAPI_DISP_GetDisplayIdByDisplayName(" + gdiName + "): " + Err(s) + " (" + s + ")");
            return id;
        }

        // ask the driver to calculate the timing (type: 1 auto, 5 CVT, 6 CVT-RB, 7 GTF); on success it becomes the pending timing
        public static int GetTiming(uint id, uint w, uint h, double hz, uint type)
        {
            TimingInput ti = new TimingInput();
            ti.version = (uint)(Marshal.SizeOf(typeof(TimingInput)) | (1 << 16));
            ti.width = w; ti.height = h; ti.rr = (float)hz; ti.type = type;
            NvTiming t = new NvTiming();
            t.etc.name = new byte[40];
            int s = Get<DGetTiming>(ID_GetTiming)(id, ref ti, ref t);
            if (s != 0) return s;
            CustomDisplay cd = Blank();
            cd.width = w; cd.height = h; cd.depth = 32; cd.colorFormat = 0;
            cd.srcPartition.x = 0f; cd.srcPartition.y = 0f; cd.srcPartition.w = 1f; cd.srcPartition.h = 1f;
            cd.xRatio = 1f; cd.yRatio = 1f;
            cd.timing = t;
            if (cd.timing.etc.name == null) cd.timing.etc.name = new byte[40];
            pending = cd;
            return 0;
        }

        // { HVisible, VVisible, HTotal, VTotal, pclk (10 kHz), rrx1k } of the pending timing
        public static double[] PendingTiming()
        {
            NvTiming t = pending.timing;
            return new double[] { t.HVisible, t.VVisible, t.HTotal, t.VTotal, t.pclk, t.etc.rrx1k };
        }

        // the timing that is sent to the monitor right now ({ HVisible, VVisible, HTotal, VTotal, pclk, rrx1k } or null);
        // does not touch the pending custom display
        public static double[] CurrentSignal(uint id, uint w, uint h, double hz)
        {
            TimingInput ti = new TimingInput();
            ti.version = (uint)(Marshal.SizeOf(typeof(TimingInput)) | (1 << 16));
            ti.width = w; ti.height = h; ti.rr = (float)hz; ti.type = 0;   // NV_TIMING_OVERRIDE_CURRENT
            NvTiming t = new NvTiming();
            t.etc.name = new byte[40];
            if (Get<DGetTiming>(ID_GetTiming)(id, ref ti, ref t) != 0) return null;
            return new double[] { t.HVisible, t.VVisible, t.HTotal, t.VTotal, t.pclk, t.etc.rrx1k };
        }

        // our own CVT-RB timing as the pending one
        public static void Prepare(uint w, uint h, double hz,
            ushort hFP, ushort hSW, ushort hTot, byte hPol,
            ushort vFP, ushort vSW, ushort vTot, byte vPol, uint pclk10k)
        {
            CustomDisplay cd = Blank();
            cd.width = w; cd.height = h; cd.depth = 32; cd.colorFormat = 0;
            cd.srcPartition.x = 0f; cd.srcPartition.y = 0f; cd.srcPartition.w = 1f; cd.srcPartition.h = 1f;
            cd.xRatio = 1f; cd.yRatio = 1f;
            cd.timing.HVisible = (ushort)w; cd.timing.HBorder = 0; cd.timing.HFrontPorch = hFP; cd.timing.HSyncWidth = hSW; cd.timing.HTotal = hTot; cd.timing.HSyncPol = hPol;
            cd.timing.VVisible = (ushort)h; cd.timing.VBorder = 0; cd.timing.VFrontPorch = vFP; cd.timing.VSyncWidth = vSW; cd.timing.VTotal = vTot; cd.timing.VSyncPol = vPol;
            cd.timing.interlaced = 0;
            cd.timing.pclk = pclk10k;
            cd.timing.etc.flag = 0;
            cd.timing.etc.rr = (ushort)Math.Round(hz);
            cd.timing.etc.rrx1k = (uint)Math.Round(hz * 1000.0);
            cd.timing.etc.aspect = 0;
            cd.timing.etc.rep = 1;
            cd.timing.etc.status = 0;
            byte[] nm = Encoding.ASCII.GetBytes("CUST:" + w + "x" + h + "x" + hz.ToString("0.###", CultureInfo.InvariantCulture) + "Hz");
            Array.Copy(nm, cd.timing.etc.name, Math.Min(nm.Length, 39));
            pending = cd;
        }

        public static int Try(uint id)
        {
            CustomDisplay cd = pending;
            return Get<DTry>(ID_TryCustomDisplay)(new uint[] { id }, 1, ref cd);
        }

        public static int Revert(uint id)
        {
            return Get<DRevert>(ID_RevertCustomDisplayTrial)(new uint[] { id }, 1);
        }

        public static int Save(uint id, uint outputOnly, uint monitorOnly)
        {
            return Get<DSave>(ID_SaveCustomDisplay)(new uint[] { id }, 1, outputOnly, monitorOnly);
        }

        // delete the custom resolution that was created last (same width/height/timing)
        public static int Delete(uint id)
        {
            CustomDisplay cd = pending;
            return Get<DDelete>(ID_DeleteCustomDisplay)(new uint[] { id }, 1, ref cd);
        }

        // removes the saved 1:1 custom resolutions of exactly this size and refresh rate (their timing is the size itself, so they are not
        // GPU-scaled modes); returns how many were removed
        public static int DeleteSameSize(uint id, uint w, uint h, double hz)
        {
            DEnum en = Get<DEnum>(ID_EnumCustomDisplay);
            DDelete del = Get<DDelete>(ID_DeleteCustomDisplay);
            int removed = 0;
            for (int round = 0; round < 16; round++)
            {
                bool found = false;
                for (uint i = 0; i < 512; i++)
                {
                    CustomDisplay cd = Blank();
                    if (en(id, i, ref cd) != 0) break;
                    if (cd.width != w || cd.height != h) continue;
                    if (Math.Abs(cd.timing.etc.rrx1k / 1000.0 - hz) >= 0.6) continue;
                    if (cd.timing.HVisible != w || cd.timing.VVisible != h) continue;
                    if (del(new uint[] { id }, 1, ref cd) == 0) { removed++; found = true; }
                    break;   // the indices shift after a deletion, so start the search again
                }
                if (!found) break;
            }
            return removed;
        }

        // is this resolution in the driver's list of saved custom resolutions?
        public static bool HasCustom(uint id, uint w, uint h, double hz)
        {
            DEnum f = Get<DEnum>(ID_EnumCustomDisplay);
            for (uint i = 0; i < 512; i++)
            {
                CustomDisplay cd = Blank();
                int s = f(id, i, ref cd);
                if (s != 0) break;
                if (cd.width == w && cd.height == h && (hz <= 0 || Math.Abs(cd.timing.etc.rrx1k / 1000.0 - hz) < 0.6)) return true;
            }
            return false;
        }

        // ---------- color settings (what "Adjust desktop color settings" uses) ----------
        private static bool ColorTarget(string gdiName, out IntPtr handle, out uint outId)
        {
            handle = IntPtr.Zero; outId = 0;
            if (Get<DGetHandle>(ID_GetAssociatedDisplayHandle)(gdiName, out handle) != 0 || handle == IntPtr.Zero) return false;
            try { if (Get<DGetOutId>(ID_GetAssociatedOutputId)(handle, out outId) != 0) outId = 0; }
            catch (Exception) { outId = 0; }
            return true;
        }

        // calls an NVAPI "get" function that fills a versioned structure; returns the bytes of the structure or null
        private static byte[] GetStruct(uint fnId, IntPtr h, uint outId, int size)
        {
            IntPtr buf = Marshal.AllocHGlobal(size + 64);
            try
            {
                Marshal.Copy(new byte[size + 64], 0, buf, size + 64);
                Marshal.WriteInt32(buf, 0, size | (1 << 16));
                if (Get<DInfoCall>(fnId)(h, outId, buf) != 0) return null;
                byte[] r = new byte[size];
                Marshal.Copy(buf, r, 0, size);
                return r;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        private static int SetStruct(uint fnId, IntPtr h, uint outId, byte[] data)
        {
            IntPtr buf = Marshal.AllocHGlobal(data.Length + 64);
            try
            {
                Marshal.Copy(new byte[data.Length + 64], 0, buf, data.Length + 64);
                Marshal.Copy(data, 0, buf, data.Length);
                Marshal.WriteInt32(buf, 0, data.Length | (1 << 16));
                return Get<DInfoCall>(fnId)(h, outId, buf);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        // reads the digital vibrance and the hue of a display into s (whatever the driver does not report stays unset)
        public static void ReadColor(string gdiName, ColorSnapshot s)
        {
            IntPtr h; uint oid;
            if (!ColorTarget(gdiName, out h, out oid)) return;
            try
            {
                byte[] r = GetStruct(ID_GetDVCInfoEx, h, oid, 20);   // the "Ex" call first (percent), the old one as a fallback
                if (r != null) { s.HasDvc = true; s.DvcEx = true; s.DvcRaw = r; s.DvcLevel = BitConverter.ToInt32(r, 4); }
            }
            catch (Exception) { }
            if (!s.HasDvc)
            {
                try
                {
                    byte[] r2 = GetStruct(ID_GetDVCInfo, h, oid, 16);
                    if (r2 != null) { s.HasDvc = true; s.DvcEx = false; s.DvcRaw = r2; s.DvcLevel = BitConverter.ToInt32(r2, 4); }
                }
                catch (Exception) { }
            }
            try
            {
                byte[] r3 = GetStruct(ID_GetHUEInfo, h, oid, 12);
                if (r3 != null) { s.HasHue = true; s.HueAngle = BitConverter.ToInt32(r3, 4); }
            }
            catch (Exception) { }
        }

        // puts back the digital vibrance (mask bit 2) and/or the hue (mask bit 4) saved in s; returns the bits written without an error
        public static int WriteColor(string gdiName, ColorSnapshot s, int mask)
        {
            int done = 0;
            IntPtr h; uint oid;
            if (!ColorTarget(gdiName, out h, out oid)) return 0;
            if ((mask & 2) != 0 && s.HasDvc)
            {
                try
                {
                    int st;
                    if (s.DvcEx) st = SetStruct(ID_SetDVCLevelEx, h, oid, s.DvcRaw);
                    else st = Get<DLevelCall>(ID_SetDVCLevel)(h, oid, s.DvcLevel);
                    if (st == 0) done |= 2;
                }
                catch (Exception) { }
            }
            if ((mask & 4) != 0 && s.HasHue)
            {
                try { if (Get<DLevelCall>(ID_SetHUEAngle)(h, oid, s.HueAngle) == 0) done |= 4; }
                catch (Exception) { }
            }
            return done;
        }

        // ---------- NVIDIA display configuration (what "Adjust desktop size and position" uses) ----------
        private static IntPtr Alloc(List<IntPtr> mem, int size)
        {
            IntPtr p = Marshal.AllocHGlobal(size);
            Marshal.Copy(new byte[size], 0, p, size);
            mem.Add(p);
            return p;
        }

        private static void FreeAll(List<IntPtr> mem)
        {
            foreach (IntPtr p in mem) Marshal.FreeHGlobal(p);
            mem.Clear();
        }

        private static IntPtr At(IntPtr b, int offset) { return new IntPtr(b.ToInt64() + offset); }

        public static NvConfig GetConfig()
        {
            DGetCfg get = Get<DGetCfg>(ID_GetDisplayConfig);
            uint count = 0;
            if (get(ref count, IntPtr.Zero) != 0 || count == 0) return null;

            int pathSize = Marshal.SizeOf(typeof(NvPathInfoN));
            int tgtSize = Marshal.SizeOf(typeof(NvPathTargetN));
            int advSize = Marshal.SizeOf(typeof(NvAdv));
            int srcSize = Marshal.SizeOf(typeof(NvSourceMode));
            uint pathVer = (uint)(pathSize | (2 << 16));
            uint advVer = (uint)(advSize | (1 << 16));

            List<IntPtr> mem = new List<IntPtr>();
            try
            {
                IntPtr paths = Alloc(mem, pathSize * (int)count);
                for (int i = 0; i < (int)count; i++)
                {
                    NvPathInfoN pn = new NvPathInfoN();
                    pn.version = pathVer;
                    Marshal.StructureToPtr(pn, At(paths, i * pathSize), false);
                }
                uint c1 = count;
                if (get(ref c1, paths) != 0) return null;

                for (int i = 0; i < (int)c1; i++)
                {
                    IntPtr pp = At(paths, i * pathSize);
                    NvPathInfoN pn = (NvPathInfoN)Marshal.PtrToStructure(pp, typeof(NvPathInfoN));
                    int tc = (int)pn.targetInfoCount;
                    if (tc > 0)
                    {
                        IntPtr tg = Alloc(mem, tgtSize * tc);
                        for (int j = 0; j < tc; j++)
                        {
                            IntPtr det = Alloc(mem, advSize);
                            NvAdv adv = new NvAdv();
                            adv.version = advVer; adv.timing = new byte[96];
                            Marshal.StructureToPtr(adv, det, false);
                            NvPathTargetN tn = new NvPathTargetN();
                            tn.details = det;
                            Marshal.StructureToPtr(tn, At(tg, j * tgtSize), false);
                        }
                        pn.targetInfo = tg;
                    }
                    pn.sourceModeInfo = Alloc(mem, srcSize);
                    pn.osAdapterId = Alloc(mem, 8);
                    Marshal.StructureToPtr(pn, pp, false);
                }
                uint c2 = c1;
                if (get(ref c2, paths) != 0) return null;

                NvConfig cfg = new NvConfig();
                for (int i = 0; i < (int)c2; i++)
                {
                    NvPathInfoN pn = (NvPathInfoN)Marshal.PtrToStructure(At(paths, i * pathSize), typeof(NvPathInfoN));
                    NvPath mp = new NvPath();
                    mp.SourceId = pn.sourceId; mp.Bits = pn.bits; mp.NonNvidia = (pn.bits & 1) != 0;
                    if (pn.osAdapterId != IntPtr.Zero) Marshal.Copy(pn.osAdapterId, mp.Luid, 0, 8);
                    if (pn.sourceModeInfo != IntPtr.Zero) mp.Src = (NvSourceMode)Marshal.PtrToStructure(pn.sourceModeInfo, typeof(NvSourceMode));
                    for (int j = 0; j < (int)pn.targetInfoCount; j++)
                    {
                        NvPathTargetN tn = (NvPathTargetN)Marshal.PtrToStructure(At(pn.targetInfo, j * tgtSize), typeof(NvPathTargetN));
                        NvTarget mt = new NvTarget();
                        mt.DisplayId = tn.displayId; mt.TargetId = tn.targetId;
                        if (tn.details != IntPtr.Zero) mt.Adv = (NvAdv)Marshal.PtrToStructure(tn.details, typeof(NvAdv));
                        else { mt.Adv = new NvAdv(); mt.Adv.version = advVer; mt.Adv.timing = new byte[96]; }
                        if (mt.Adv.timing == null) mt.Adv.timing = new byte[96];
                        mp.Targets.Add(mt);
                    }
                    cfg.Paths.Add(mp);
                }
                return cfg;
            }
            finally { FreeAll(mem); }
        }

        // flags: 1 = validate only, 2 = save to persistence, 8 = refresh the OS mode list
        public static int SetConfig(NvConfig cfg, uint flags)
        {
            DSetCfg set = Get<DSetCfg>(ID_SetDisplayConfig);
            int pathSize = Marshal.SizeOf(typeof(NvPathInfoN));
            int tgtSize = Marshal.SizeOf(typeof(NvPathTargetN));
            int advSize = Marshal.SizeOf(typeof(NvAdv));
            uint pathVer = (uint)(pathSize | (2 << 16));
            uint advVer = (uint)(advSize | (1 << 16));

            List<IntPtr> mem = new List<IntPtr>();
            try
            {
                IntPtr paths = Alloc(mem, pathSize * cfg.Paths.Count);
                for (int i = 0; i < cfg.Paths.Count; i++)
                {
                    NvPath mp = cfg.Paths[i];
                    NvPathInfoN pn = new NvPathInfoN();
                    pn.version = pathVer; pn.sourceId = mp.SourceId; pn.targetInfoCount = (uint)mp.Targets.Count; pn.bits = mp.Bits;
                    if (mp.Targets.Count > 0)
                    {
                        IntPtr tg = Alloc(mem, tgtSize * mp.Targets.Count);
                        for (int j = 0; j < mp.Targets.Count; j++)
                        {
                            NvTarget mt = mp.Targets[j];
                            NvAdv adv = mt.Adv;
                            if (adv.version == 0) adv.version = advVer;
                            if (adv.timing == null) adv.timing = new byte[96];
                            IntPtr det = Alloc(mem, advSize);
                            Marshal.StructureToPtr(adv, det, false);
                            NvPathTargetN tn = new NvPathTargetN();
                            tn.displayId = mt.DisplayId; tn.details = det; tn.targetId = mt.TargetId;
                            Marshal.StructureToPtr(tn, At(tg, j * tgtSize), false);
                        }
                        pn.targetInfo = tg;
                    }
                    IntPtr src = Alloc(mem, Marshal.SizeOf(typeof(NvSourceMode)));
                    Marshal.StructureToPtr(mp.Src, src, false);
                    pn.sourceModeInfo = src;
                    IntPtr luid = Alloc(mem, 8);
                    Marshal.Copy(mp.Luid, 0, luid, 8);
                    pn.osAdapterId = luid;
                    Marshal.StructureToPtr(pn, At(paths, i * pathSize), false);
                }
                return set((uint)cfg.Paths.Count, paths, flags);
            }
            finally { FreeAll(mem); }
        }
    }

    // =====================================================================
    //  GDI: displays and modes
    // =====================================================================
    public class DispInfo
    {
        public string Name; public string Desc; public bool Primary;
        public string Monitor = "";                       // model code of the monitor, for example GSM5B09 (empty if unknown)
        public int W; public int H; public int Hz;
    }

    // a display that is attached to the desktop, with its complete mode and place (to bring it back if Windows switches it off)
    public class DisplayState
    {
        public string Name;
        public bool Primary;
        public int X, Y, W, H, Hz;
        public Gdi.DEVMODE Dm;
    }

    public class DisplaySnapshot
    {
        public List<DisplayState> Items = new List<DisplayState>();
        public int PrimW, PrimH;   // the primary display always sits at 0,0
    }

    // the real Windows build (Environment.OSVersion lies for programs without a compatibility manifest)
    public static class WinInfo
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OSVERSIONINFOEX
        {
            public int dwOSVersionInfoSize; public int dwMajorVersion; public int dwMinorVersion; public int dwBuildNumber; public int dwPlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szCSDVersion;
            public ushort wServicePackMajor; public ushort wServicePackMinor; public ushort wSuiteMask; public byte wProductType; public byte wReserved;
        }
        [DllImport("ntdll.dll")]
        private static extern int RtlGetVersion(ref OSVERSIONINFOEX v);

        public static int Build()
        {
            try
            {
                OSVERSIONINFOEX v = new OSVERSIONINFOEX();
                v.dwOSVersionInfoSize = Marshal.SizeOf(typeof(OSVERSIONINFOEX));
                if (RtlGetVersion(ref v) == 0) return v.dwBuildNumber;
            }
            catch (Exception) { }
            return 0;
        }
    }

    public static class Gdi
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public ushort dmSpecVersion; public ushort dmDriverVersion; public ushort dmSize; public ushort dmDriverExtra;
            public uint dmFields;
            public int dmPositionX; public int dmPositionY;
            public uint dmDisplayOrientation; public uint dmDisplayFixedOutput;
            public short dmColor; public short dmDuplex; public short dmYResolution; public short dmTTOption; public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public ushort dmLogPixels;
            public uint dmBitsPerPel; public uint dmPelsWidth; public uint dmPelsHeight; public uint dmDisplayFlags; public uint dmDisplayFrequency;
            public uint dmICMMethod; public uint dmICMIntent; public uint dmMediaType; public uint dmDitherType;
            public uint dmReserved1; public uint dmReserved2; public uint dmPanningWidth; public uint dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevicesW(string device, uint index, ref DISPLAY_DEVICE dd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettingsW(string device, int modeNum, ref DEVMODE dm);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ChangeDisplaySettingsExW(string device, ref DEVMODE dm, IntPtr hwnd, uint flags, IntPtr lParam);

        // the same call without a mode: applies the changes that were collected with CDS_NORESET
        [DllImport("user32.dll", EntryPoint = "ChangeDisplaySettingsExW", CharSet = CharSet.Unicode)]
        private static extern int ChangeDisplaySettingsExCommit(string device, IntPtr dm, IntPtr hwnd, uint flags, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern int SetDisplayConfig(uint numPaths, IntPtr paths, uint numModes, IntPtr modes, uint flags);

        private static DEVMODE NewDm()
        {
            DEVMODE dm = new DEVMODE();
            dm.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE));
            return dm;
        }

        // current mode: { width, height, refresh } or null
        public static int[] Current(string dev)
        {
            DEVMODE dm = NewDm();
            if (!EnumDisplaySettingsW(dev, -1, ref dm)) return null;
            return new int[] { (int)dm.dmPelsWidth, (int)dm.dmPelsHeight, (int)dm.dmDisplayFrequency };
        }

        // the id of the monitor that is connected to this display, like MONITOR\GSM5B09\{4d36e96e-e325-11ce-bfc1-08002be10318}\0007
        public static string MonitorDeviceId(string gdiName)
        {
            DISPLAY_DEVICE mon = new DISPLAY_DEVICE();
            mon.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
            if (!EnumDisplayDevicesW(gdiName, 0, ref mon, 0)) return null;
            return mon.DeviceID;
        }

        // the highest refresh rate that Windows lists for this resolution on this display (0 = no such mode in the list)
        public static int MaxRefresh(string dev, int w, int h)
        {
            int max = 0;
            for (int i = 0; i < 4096; i++)
            {
                DEVMODE dm = NewDm();
                if (!EnumDisplaySettingsW(dev, i, ref dm)) break;
                if (dm.dmPelsWidth == (uint)w && dm.dmPelsHeight == (uint)h && (int)dm.dmDisplayFrequency > max) max = (int)dm.dmDisplayFrequency;
            }
            return max;
        }

        // active desktop displays
        public static List<DispInfo> Displays()
        {
            List<DispInfo> list = new List<DispInfo>();
            for (uint i = 0; i < 32; i++)
            {
                DISPLAY_DEVICE dd = new DISPLAY_DEVICE();
                dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                if (!EnumDisplayDevicesW(null, i, ref dd, 0)) break;
                if ((dd.StateFlags & 0x1) == 0) continue;
                DispInfo d = new DispInfo();
                d.Name = dd.DeviceName; d.Desc = dd.DeviceString; d.Primary = (dd.StateFlags & 0x4) != 0;
                d.Monitor = "";
                try
                {
                    DISPLAY_DEVICE mon = new DISPLAY_DEVICE();
                    mon.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                    if (EnumDisplayDevicesW(dd.DeviceName, 0, ref mon, 0))
                    {
                        string[] part = (mon.DeviceID ?? "").Split('\\');
                        d.Monitor = (part.Length >= 2 && part[0] == "MONITOR") ? part[1] : (mon.DeviceString ?? "");
                    }
                }
                catch (Exception) { d.Monitor = ""; }
                int[] c = Current(dd.DeviceName);
                if (c != null) { d.W = c[0]; d.H = c[1]; d.Hz = c[2]; }
                list.Add(d);
            }
            return list;
        }

        // apply a mode through Windows (persist = write to the registry so it survives a reboot)
        public static int SetMode(string dev, int w, int h, int hz, bool persist)
        {
            DEVMODE dm = NewDm();
            if (!EnumDisplaySettingsW(dev, -1, ref dm)) return -100;
            dm.dmPelsWidth = (uint)w; dm.dmPelsHeight = (uint)h; dm.dmDisplayFrequency = (uint)hz;
            // PELSWIDTH | PELSHEIGHT | DISPLAYFREQUENCY | BITSPERPEL | POSITION | DISPLAYORIENTATION
            dm.dmFields = 0x80000 | 0x100000 | 0x400000 | 0x40000 | 0x20 | 0x80;
            return ChangeDisplaySettingsExW(dev, ref dm, IntPtr.Zero, persist ? 1u : 0u, IntPtr.Zero);
        }

        // every display that is attached to the desktop now, with its complete mode and place
        public static DisplaySnapshot Snapshot()
        {
            DisplaySnapshot snap = new DisplaySnapshot();
            for (uint i = 0; i < 32; i++)
            {
                DISPLAY_DEVICE dd = new DISPLAY_DEVICE();
                dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                if (!EnumDisplayDevicesW(null, i, ref dd, 0)) break;
                if ((dd.StateFlags & 0x1) == 0) continue;
                DEVMODE dm = NewDm();
                if (!EnumDisplaySettingsW(dd.DeviceName, -1, ref dm)) continue;
                DisplayState st = new DisplayState();
                st.Name = dd.DeviceName;
                st.Primary = (dd.StateFlags & 0x4) != 0;
                st.X = dm.dmPositionX; st.Y = dm.dmPositionY;
                st.W = (int)dm.dmPelsWidth; st.H = (int)dm.dmPelsHeight; st.Hz = (int)dm.dmDisplayFrequency;
                st.Dm = dm;
                snap.Items.Add(st);
                if (st.Primary) { snap.PrimW = st.W; snap.PrimH = st.H; }
            }
            return snap;
        }

        // the displays of the snapshot that are not attached to the desktop any more
        public static List<DisplayState> Missing(DisplaySnapshot snap)
        {
            List<DisplayState> miss = new List<DisplayState>();
            List<DispInfo> now = Displays();
            foreach (DisplayState st in snap.Items)
            {
                bool found = false;
                foreach (DispInfo d in now)
                {
                    if (string.Equals(d.Name, st.Name, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
                }
                if (!found) miss.Add(st);
            }
            return miss;
        }

        // switches the missing displays on again with the mode they had. The primary display may have another size now, so a display
        // that sat right of / below it moves with its edge (it keeps touching the primary on the same side with the same gap).
        public static int Reattach(DisplaySnapshot snap, List<DisplayState> missing)
        {
            if (missing == null || missing.Count == 0) return 0;
            int curW = snap.PrimW, curH = snap.PrimH;
            foreach (DispInfo d in Displays())
            {
                if (d.Primary && d.W > 0 && d.H > 0) { curW = d.W; curH = d.H; break; }
            }
            int done = 0;
            foreach (DisplayState st in missing)
            {
                DEVMODE dm = st.Dm;
                dm.dmPositionX = (st.X >= snap.PrimW) ? st.X - snap.PrimW + curW : st.X;
                dm.dmPositionY = (st.Y >= snap.PrimH) ? st.Y - snap.PrimH + curH : st.Y;
                // PELSWIDTH | PELSHEIGHT | DISPLAYFREQUENCY | BITSPERPEL | POSITION | DISPLAYORIENTATION
                dm.dmFields = 0x80000 | 0x100000 | 0x400000 | 0x40000 | 0x20 | 0x80;
                // CDS_UPDATEREGISTRY | CDS_NORESET: the changes are collected and applied together below
                if (ChangeDisplaySettingsExW(st.Name, ref dm, IntPtr.Zero, 0x1u | 0x10000000u, IntPtr.Zero) == 0) done++;
            }
            ChangeDisplaySettingsExCommit(null, IntPtr.Zero, IntPtr.Zero, 0u, IntPtr.Zero);
            return done;
        }

        // "extend these displays" (what Win+P > Extend does): switches on every connected display
        public static int ExtendTopology()
        {
            return SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, 0x80u | 0x4u);   // SDC_APPLY | SDC_TOPOLOGY_EXTEND
        }

        // extends the desktop if a display is connected but not attached (used after the monitor devices were enabled again)
        public static bool ExtendIfInactive()
        {
            int attached = 0, present = 0;
            for (uint i = 0; i < 32; i++)
            {
                DISPLAY_DEVICE dd = new DISPLAY_DEVICE();
                dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                if (!EnumDisplayDevicesW(null, i, ref dd, 0)) break;
                if ((dd.StateFlags & 0x8) != 0) continue;      // mirroring driver (remote / virtual display)
                present++;
                if ((dd.StateFlags & 0x1) != 0) attached++;
            }
            if (present <= attached) return false;
            return ExtendTopology() == 0;
        }
    }

    // =====================================================================
    //  The resolution the user started with. It is remembered between runs (it is the first preset in the window)
    //  in %ProgramData%\nv_custom_res_original.txt. Delete that file to make the program forget it.
    // =====================================================================
    public class OriginalMode
    {
        public int W, H, Hz;
        public string Desc = "", Monitor = "";

        public static string StateFile
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "nv_custom_res_original.txt"); }
        }

        public static OriginalMode Load()
        {
            try
            {
                if (!File.Exists(StateFile)) return null;
                string[] lines = File.ReadAllLines(StateFile, Encoding.UTF8);
                if (lines.Length == 0) return null;
                string[] p = lines[0].Split('\t');
                if (p.Length < 3) return null;
                OriginalMode m = new OriginalMode();
                m.W = int.Parse(p[0], CultureInfo.InvariantCulture);
                m.H = int.Parse(p[1], CultureInfo.InvariantCulture);
                m.Hz = int.Parse(p[2], CultureInfo.InvariantCulture);
                if (p.Length > 3) m.Desc = p[3];
                if (p.Length > 4) m.Monitor = p[4];
                return (m.W >= 320 && m.H >= 200) ? m : null;
            }
            catch (Exception) { return null; }
        }

        public void Save()
        {
            string line = W.ToString(CultureInfo.InvariantCulture) + "\t" + H.ToString(CultureInfo.InvariantCulture) + "\t" + Hz.ToString(CultureInfo.InvariantCulture)
                + "\t" + (Desc ?? "").Replace('\t', ' ') + "\t" + (Monitor ?? "").Replace('\t', ' ');
            File.WriteAllText(StateFile, line + "\r\n", new UTF8Encoding(false));
        }

        // the same graphics card and (if both are known) the same monitor
        private bool Same(string desc, string monitor)
        {
            if ((desc ?? "") != Desc) return false;
            string mon = monitor ?? "";
            return Monitor.Length == 0 || mon.Length == 0 || Monitor == mon;
        }

        // Decides which resolution counts as the original one and remembers it.
        //  desk* = the mode of the desktop now, sig* = the signal that really goes to the monitor (0 if unknown).
        //  A GPU-stretched desktop is smaller than the signal, and the signal is then the monitor's own size.
        public static OriginalMode Resolve(string desc, string monitor, int deskW, int deskH, int deskHz, int sigW, int sigH)
        {
            OriginalMode saved = Load();
            if (deskW <= 0 || deskH <= 0) return saved;
            bool stretched = sigW >= deskW && sigH >= deskH && (sigW > deskW || sigH > deskH);
            int nw = stretched ? sigW : deskW;
            int nh = stretched ? sigH : deskH;
            if (saved != null && saved.Same(desc, monitor))
            {
                bool keep;
                if (stretched) keep = (saved.W == nw && saved.H == nh);                     // the monitor's own size wins
                else keep = ((long)deskW * deskH <= (long)saved.W * saved.H);               // a bigger desktop than the "original" means a new monitor
                if (keep)
                {
                    bool upd = false;
                    if (!stretched && deskW == saved.W && deskH == saved.H && deskHz > 0 && deskHz != saved.Hz) { saved.Hz = deskHz; upd = true; }
                    if (saved.Monitor.Length == 0 && (monitor ?? "").Length > 0) { saved.Monitor = monitor; upd = true; }
                    if (upd) { try { saved.Save(); } catch (Exception) { } }
                    return saved;
                }
            }
            OriginalMode m = new OriginalMode();
            m.W = nw; m.H = nh; m.Hz = deskHz; m.Desc = desc ?? ""; m.Monitor = monitor ?? "";
            try { m.Save(); } catch (Exception) { }
            return m;
        }
    }

    // =====================================================================
    //  Color settings of the primary display: brightness, contrast, gamma, digital vibrance, hue.
    //  The driver can reset them when the mode or the display configuration changes or when the monitors
    //  are switched off and on, so they are saved first and put back afterwards (only if they changed).
    // =====================================================================
    public class ColorSnapshot
    {
        public string Device;
        public ushort[] Ramp;                         // gamma ramp = brightness, contrast, gamma (3 x 256 values) or null
        public bool HasDvc, DvcEx;                    // digital vibrance (what NVIDIA Control Panel calls "Digital vibrance")
        public byte[] DvcRaw;
        public int DvcLevel;
        public bool HasHue;
        public int HueAngle;
        // 1 = gamma ramp, 2 = digital vibrance, 4 = hue
        public int Mask { get { return (Ramp != null ? 1 : 0) | (HasDvc ? 2 : 0) | (HasHue ? 4 : 0); } }
    }

    public static class GammaRamp
    {
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateDCW(string driver, string device, string port, IntPtr devmode);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")]
        private static extern bool GetDeviceGammaRamp(IntPtr hdc, [In, Out] ushort[] ramp);
        [DllImport("gdi32.dll")]
        private static extern bool SetDeviceGammaRamp(IntPtr hdc, [In, Out] ushort[] ramp);

        public static ushort[] Get(string dev)
        {
            IntPtr dc = CreateDCW("DISPLAY", dev, null, IntPtr.Zero);
            if (dc == IntPtr.Zero) return null;
            try
            {
                ushort[] r = new ushort[768];
                return GetDeviceGammaRamp(dc, r) ? r : null;
            }
            finally { DeleteDC(dc); }
        }

        public static bool Set(string dev, ushort[] ramp)
        {
            if (ramp == null || ramp.Length != 768) return false;
            IntPtr dc = CreateDCW("DISPLAY", dev, null, IntPtr.Zero);
            if (dc == IntPtr.Zero) return false;
            try { return SetDeviceGammaRamp(dc, (ushort[])ramp.Clone()); }
            finally { DeleteDC(dc); }
        }

        // the driver may store the ramp with 8-bit precision, so a difference of about one 8-bit step does not count
        public static bool Same(ushort[] a, ushort[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (Math.Abs(a[i] - b[i]) > 300) return false;
            }
            return true;
        }
    }

    public static class Colors
    {
        public static ColorSnapshot Save(string dev)
        {
            ColorSnapshot s = new ColorSnapshot();
            s.Device = dev;
            try { s.Ramp = GammaRamp.Get(dev); } catch (Exception) { s.Ramp = null; }
            try { NvApi.ReadColor(dev, s); } catch (Exception) { }
            return s;
        }

        // which parts of the snapshot differ from what the driver has now (1 = gamma ramp, 2 = vibrance, 4 = hue)
        public static int Diff(ColorSnapshot s)
        {
            int mask = 0;
            ColorSnapshot cur = new ColorSnapshot();
            cur.Device = s.Device;
            try { cur.Ramp = GammaRamp.Get(s.Device); } catch (Exception) { cur.Ramp = null; }
            try { NvApi.ReadColor(s.Device, cur); } catch (Exception) { }
            if (s.Ramp != null && cur.Ramp != null && !GammaRamp.Same(s.Ramp, cur.Ramp)) mask |= 1;
            if (s.HasDvc && cur.HasDvc && s.DvcLevel != cur.DvcLevel) mask |= 2;
            if (s.HasHue && cur.HasHue && s.HueAngle != cur.HueAngle) mask |= 4;
            return mask;
        }

        // writes back only what differs; returns the mask of the parts that differed (0 = nothing to do)
        public static int Restore(ColorSnapshot s)
        {
            int mask = Diff(s);
            if ((mask & 1) != 0)
            {
                try { GammaRamp.Set(s.Device, s.Ramp); } catch (Exception) { }
            }
            if ((mask & 6) != 0)
            {
                try { NvApi.WriteColor(s.Device, s, mask & 6); } catch (Exception) { }
            }
            return mask;
        }
    }

    // =====================================================================
    //  Monitors in Device Manager (the device/driver only, never the physical monitor)
    // =====================================================================
    public static class Monitors
    {
        public static string StateFile
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "nv_custom_res_monitors.txt"); }
        }

        public static bool HasSaved() { return File.Exists(StateFile); }

        private static List<string> RunPs(string script)
        {
            string full = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; $ErrorActionPreference='Stop'; " + script;
            string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            psi.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + enc;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            List<string> lines = new List<string>();
            using (Process pr = Process.Start(psi))
            {
                string err = null;
                pr.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) err = e.Data; };
                pr.BeginErrorReadLine();
                string line;
                while ((line = pr.StandardOutput.ReadLine()) != null) { if (line.Length > 0) lines.Add(line); }
                pr.WaitForExit();
                if (pr.ExitCode != 0 && err != null) lines.Add("ERR||" + err);
            }
            return lines;
        }

        private static string Q(string s) { return "'" + s.Replace("'", "''") + "'"; }

        // returns how many monitor devices were disabled; they are remembered in StateFile
        public static int DisableAll(Action<string, int> log)
        {
            List<string> lines = RunPs(
                "Get-PnpDevice -Class Monitor -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq 'OK' } | ForEach-Object { " +
                "try { Disable-PnpDevice -InstanceId $_.InstanceId -Confirm:$false -ErrorAction Stop; 'OK|' + $_.InstanceId + '|' + $_.FriendlyName } " +
                "catch { 'ERR|' + $_.InstanceId + '|' + $_.FriendlyName + '|' + $_.Exception.Message } }");
            List<string> done = new List<string>();
            int total = 0;
            foreach (string l in lines)
            {
                string[] p = l.Split('|');
                if (p.Length >= 3 && p[0] == "OK") { done.Add(p[1]); total++; log(Strings.T("mon_disabled", p[2], p[1]), 0); }
                else if (p.Length >= 3 && p[0] == "ERR") { total++; log(Strings.T("mon_disable_fail", p[2], p.Length > 3 ? p[3] : ""), 2); }
            }
            if (total == 0) log(Strings.T("no_monitors"), 2);
            if (done.Count > 0) File.WriteAllLines(StateFile, done.ToArray(), new UTF8Encoding(false));
            return done.Count;
        }

        // The PnP instance id (DISPLAY\GSM5B09\5&...) of the monitor that is connected to this display, or null. The monitor id that Windows
        // gives for a display ends with the "Driver" value of the PnP device; the registry tells which instance has it.
        public static string InstanceOf(string gdiName)
        {
            try
            {
                string id = Gdi.MonitorDeviceId(gdiName);
                if (string.IsNullOrEmpty(id)) return null;
                string[] part = id.Split('\\');                           // MONITOR \ GSM5B09 \ {class guid} \ 0007
                if (part.Length < 4 || !string.Equals(part[0], "MONITOR", StringComparison.OrdinalIgnoreCase)) return null;
                string driver = part[2] + "\\" + part[3];
                using (RegistryKey model = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY\" + part[1]))
                {
                    if (model == null) return null;
                    foreach (string inst in model.GetSubKeyNames())
                    {
                        using (RegistryKey k = model.OpenSubKey(inst))
                        {
                            if (k == null) continue;
                            string d = k.GetValue("Driver") as string;
                            if (d != null && string.Equals(d, driver, StringComparison.OrdinalIgnoreCase)) return "DISPLAY\\" + part[1] + "\\" + inst;
                        }
                    }
                }
            }
            catch (Exception) { }
            return null;
        }

        // disables only the monitor device of one display (the other displays are not touched); it is remembered in StateFile
        public static int DisableFor(string gdiName, Action<string, int> log)
        {
            string inst = InstanceOf(gdiName);
            if (inst == null) { log(Strings.T("mon_primary_unknown", gdiName), 2); return 0; }
            List<string> lines = RunPs(
                "try { Disable-PnpDevice -InstanceId " + Q(inst) + " -Confirm:$false -ErrorAction Stop; 'OK|' + " + Q(inst) + " } " +
                "catch { 'ERR|' + " + Q(inst) + " + '|' + $_.Exception.Message }");
            int done = 0;
            foreach (string l in lines)
            {
                string[] p = l.Split('|');
                if (p.Length >= 2 && p[0] == "OK") { done++; log(Strings.T("mon_disabled", gdiName, p[1]), 0); }
                else if (p.Length >= 2 && p[0] == "ERR") log(Strings.T("mon_disable_fail", gdiName, p.Length > 2 ? p[2] : ""), 2);
            }
            if (done > 0) File.WriteAllLines(StateFile, new string[] { inst }, new UTF8Encoding(false));
            return done;
        }

        public static void RestoreAll(Action<string, int> log)
        {
            if (!File.Exists(StateFile)) return;
            string[] ids = File.ReadAllLines(StateFile);
            StringBuilder sb = new StringBuilder();
            foreach (string id in ids)
            {
                if (id.Trim().Length == 0) continue;
                sb.Append("try { Enable-PnpDevice -InstanceId " + Q(id.Trim()) + " -Confirm:$false -ErrorAction Stop; 'OK|' + " + Q(id.Trim()) + " } " +
                          "catch { 'ERR|' + " + Q(id.Trim()) + " + '|' + $_.Exception.Message }; ");
            }
            bool allOk = true;
            if (sb.Length > 0)
            {
                foreach (string l in RunPs(sb.ToString()))
                {
                    string[] p = l.Split('|');
                    if (p.Length >= 2 && p[0] == "OK") log(Strings.T("mon_enabled", p[1]), 0);
                    else if (p.Length >= 2 && p[0] == "ERR") { allOk = false; log(Strings.T("mon_enable_fail", p[1], p.Length > 2 ? p[2] : ""), 2); }
                }
            }
            if (allOk) { try { File.Delete(StateFile); } catch (Exception) { } }
            // Windows 10/11 can leave a display switched off after its monitor device was disabled and enabled again:
            // if a display is connected but not on the desktop, the desktop is extended onto it
            try
            {
                System.Threading.Thread.Sleep(2500);
                if (Gdi.ExtendIfInactive()) log(Strings.T("disp_extended"), 1);
            }
            catch (Exception) { }
        }
    }

    // =====================================================================
    //  Platform abstraction (so that the flow can be tested without Windows/NVIDIA)
    // =====================================================================
    public interface IPlatform
    {
        bool Is64Bit();
        List<DispInfo> Displays();
        int[] CurrentMode(string gdiName);
        int SetWindowsMode(string gdiName, int w, int h, int hz, bool persist);
        void NvInit();
        uint NvDisplayId(string gdiName);
        int NvGetTiming(uint id, uint w, uint h, double hz, uint type);
        double[] NvPendingTiming();
        double[] NvCurrentSignal(uint id, uint w, uint h, double hz);
        void NvPrepareManual(Timing t);
        int NvTry(uint id);
        int NvSave(uint id, uint outputOnly, uint monitorOnly);
        int NvRevert(uint id);
        int NvDeleteCustom(uint id);
        bool NvHasCustom(uint id, uint w, uint h, double hz);
        string NvErr(int status);
        NvConfig NvGetConfig();
        int NvSetConfig(NvConfig cfg, uint flags);
        int DisableMonitors(Action<string, int> log);
        void RestoreMonitors(Action<string, int> log);
        bool HasSavedMonitors();
        void Sleep(int ms);
    }

    // optional part of the platform: a platform that does not implement it simply skips the color handling
    public interface IColorPlatform
    {
        ColorSnapshot ColorSave(string gdiName);
        int ColorRestore(ColorSnapshot snap);     // writes back what differs, returns the mask of the parts that differed
        int ColorDiff(ColorSnapshot snap);        // the mask of the parts that differ now
    }

    // optional part of the platform: what the monitor itself reports as possible
    public interface IModePlatform
    {
        int MaxRefresh(string gdiName, int w, int h);
        int NvDeleteSameSize(uint id, uint w, uint h, double hz);   // removes saved 1:1 custom resolutions of this size and refresh rate
    }

    // optional part of the platform: disables only the monitor device of one display
    public interface IMonitorPlatform
    {
        int DisableMonitorFor(string gdiName, Action<string, int> log);
    }

    // optional part of the platform: keeps every display that was on before switched on
    public interface IDisplayPlatform
    {
        DisplaySnapshot DisplaysSave();
        List<DisplayState> DisplaysMissing(DisplaySnapshot snap);
        int DisplaysReattach(DisplaySnapshot snap, List<DisplayState> missing);
        int DisplaysExtend();
    }

    public class WinPlatform : IPlatform, IColorPlatform, IModePlatform, IDisplayPlatform, IMonitorPlatform
    {
        public int DisableMonitorFor(string n, Action<string, int> log) { return Monitors.DisableFor(n, log); }
        public DisplaySnapshot DisplaysSave() { return Gdi.Snapshot(); }
        public List<DisplayState> DisplaysMissing(DisplaySnapshot snap) { return Gdi.Missing(snap); }
        public int DisplaysReattach(DisplaySnapshot snap, List<DisplayState> missing) { return Gdi.Reattach(snap, missing); }
        public int DisplaysExtend() { return Gdi.ExtendTopology(); }
        public int MaxRefresh(string n, int w, int h) { return Gdi.MaxRefresh(n, w, h); }
        public int NvDeleteSameSize(uint id, uint w, uint h, double hz) { return NvApi.DeleteSameSize(id, w, h, hz); }
        public ColorSnapshot ColorSave(string n) { return Colors.Save(n); }
        public int ColorRestore(ColorSnapshot s) { return Colors.Restore(s); }
        public int ColorDiff(ColorSnapshot s) { return Colors.Diff(s); }
        public bool Is64Bit() { return Environment.Is64BitProcess; }
        public List<DispInfo> Displays() { return Gdi.Displays(); }
        public int[] CurrentMode(string n) { return Gdi.Current(n); }
        public int SetWindowsMode(string n, int w, int h, int hz, bool persist) { return Gdi.SetMode(n, w, h, hz, persist); }
        public void NvInit() { NvApi.Init(); }
        public uint NvDisplayId(string n) { return NvApi.DisplayId(n); }
        public int NvGetTiming(uint id, uint w, uint h, double hz, uint type) { return NvApi.GetTiming(id, w, h, hz, type); }
        public double[] NvPendingTiming() { return NvApi.PendingTiming(); }
        public double[] NvCurrentSignal(uint id, uint w, uint h, double hz) { return NvApi.CurrentSignal(id, w, h, hz); }
        public void NvPrepareManual(Timing t)
        {
            NvApi.Prepare((uint)t.W, (uint)t.H, t.Hz, (ushort)t.HFP, (ushort)t.HSW, (ushort)t.HTotal, (byte)t.HPol,
                          (ushort)t.VFP, (ushort)t.VSW, (ushort)t.VTotal, (byte)t.VPol, t.Pclk10k);
        }
        public int NvTry(uint id) { return NvApi.Try(id); }
        public int NvSave(uint id, uint a, uint b) { return NvApi.Save(id, a, b); }
        public int NvRevert(uint id) { return NvApi.Revert(id); }
        public int NvDeleteCustom(uint id) { return NvApi.Delete(id); }
        public bool NvHasCustom(uint id, uint w, uint h, double hz) { return NvApi.HasCustom(id, w, h, hz); }
        public string NvErr(int s) { return NvApi.Err(s); }
        public NvConfig NvGetConfig() { return NvApi.GetConfig(); }
        public int NvSetConfig(NvConfig c, uint flags) { return NvApi.SetConfig(c, flags); }
        public int DisableMonitors(Action<string, int> log) { return Monitors.DisableAll(log); }
        public void RestoreMonitors(Action<string, int> log) { Monitors.RestoreAll(log); }
        public bool HasSavedMonitors() { return Monitors.HasSaved(); }
        public void Sleep(int ms) { System.Threading.Thread.Sleep(ms); }
    }

    // =====================================================================
    //  Layout of the displays when the primary one changes its size
    // =====================================================================
    public static class Layout
    {
        public const uint ScalingFullScreenGpu = 2;   // NV_SCALING_GPU_SCALING_TO_NATIVE

        private static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        // index of the GDI-primary path of the display with this id, or -1 (also -1 if a non-NVIDIA adapter is involved)
        public static int FindPrimary(NvConfig cfg, uint displayId)
        {
            if (cfg == null) return -1;
            foreach (NvPath p in cfg.Paths) { if (p.NonNvidia) return -1; }
            int fallback = -1;
            for (int i = 0; i < cfg.Paths.Count; i++)
            {
                NvPath p = cfg.Paths[i];
                if (!p.IsGdiPrimary || p.Targets.Count == 0) continue;
                foreach (NvTarget t in p.Targets) { if (t.DisplayId == displayId) return i; }
                if (fallback < 0) fallback = i;
            }
            return fallback;
        }

        // the original configuration with the primary display at newW x newH, full-screen GPU scaling,
        // and every other display moved so that it still touches the primary one on the same side
        public static NvConfig BuildStretch(NvConfig orig, int pi, int newW, int newH, double hz)
        {
            NvConfig c = orig.Clone();
            NvPath pp = c.Paths[pi];
            int oldW = (int)pp.Src.width, oldH = (int)pp.Src.height;
            int px = pp.Src.posX, py = pp.Src.posY;
            for (int i = 0; i < c.Paths.Count; i++)
            {
                if (i == pi) continue;
                NvPath q = c.Paths[i];
                int qx = q.Src.posX - px, qy = q.Src.posY - py;
                int qw = (int)q.Src.width, qh = (int)q.Src.height;
                int nx = qx, ny = qy;
                if (qx >= oldW) { nx = qx - oldW + newW; ny = Clamp(qy, -(qh - 1), newH - 1); }          // right of the primary
                else if (qx + qw <= 0) { nx = qx; ny = Clamp(qy, -(qh - 1), newH - 1); }                 // left of the primary
                else if (qy >= oldH) { ny = qy - oldH + newH; nx = Clamp(qx, -(qw - 1), newW - 1); }     // below the primary
                else if (qy + qh <= 0) { ny = qy; nx = Clamp(qx, -(qw - 1), newW - 1); }                 // above the primary
                q.Src.posX = px + nx; q.Src.posY = py + ny;
            }
            pp.Src.width = (uint)newW; pp.Src.height = (uint)newH;
            foreach (NvTarget t in pp.Targets)
            {
                t.Adv.scaling = ScalingFullScreenGpu;
                t.Adv.refreshRate1K = (uint)Math.Round(hz * 1000.0);
            }
            return c;
        }

        private static NvPath Match(NvConfig cfg, NvPath like)
        {
            foreach (NvPath p in cfg.Paths)
            {
                if (p.Targets.Count == 0 || like.Targets.Count == 0) continue;
                if (p.Targets[0].DisplayId == like.Targets[0].DisplayId) return p;
            }
            return null;
        }

        // does "now" have the same sizes, positions and scaling as "want"?
        public static bool Matches(NvConfig now, NvConfig want)
        {
            if (now == null || want == null) return false;
            foreach (NvPath w in want.Paths)
            {
                NvPath n = Match(now, w);
                if (n == null) return false;
                if (n.Src.width != w.Src.width || n.Src.height != w.Src.height) return false;
                if (n.Src.posX != w.Src.posX || n.Src.posY != w.Src.posY) return false;
                if (w.IsGdiPrimary && n.Targets.Count > 0 && w.Targets.Count > 0 && n.Targets[0].Adv.scaling != w.Targets[0].Adv.scaling) return false;
            }
            return true;
        }

        public static string ScalingName(uint s)
        {
            switch (s)
            {
                case 1: return Strings.T("sc_1");
                case 2: return Strings.T("sc_2");
                case 3: return Strings.T("sc_3");
                case 5: return Strings.T("sc_5");
                case 6: return Strings.T("sc_6");
                case 7: return Strings.T("sc_7");
                case 8: return Strings.T("sc_8");
                default: return Strings.T("sc_other", s);
            }
        }
    }

    // =====================================================================
    //  The flow: create -> stretch -> check -> confirm -> save (only the primary display is touched)
    // =====================================================================
    public class Options
    {
        public int Width, Height;
        public double Hz;
        public bool DisableMonitors = true;
        public bool Stretch = true;
        public bool DisablePrimaryOnly = false;           // true: disable only the monitor device of the primary display (the default is all of them)
        public bool AllowOverRefresh = false;  // skip the check "refresh rate <= what the monitor lists for its native resolution"
        public bool KeepColors = true;         // save brightness/contrast/gamma/vibrance/hue and put them back if the driver resets them
        public bool AskReenable = true;        // console: ask at the end; GUI: use ReenableMonitors
        public bool ReenableMonitors = true;
        public int TestSeconds = 15;
        public string RestoreHint = "/restore";   // how the user can bring the monitors back (shown when they stay disabled)
        public uint SaveOutputOnly = 0, SaveMonitorOnly = 0;
    }

    public class Callbacks
    {
        public Action<string, int> Log = delegate(string s, int level) { };
        public Func<string, int, bool> ConfirmKeep = delegate(string s, int sec) { return true; };
        public Func<string, bool> AskYesNo = delegate(string s) { return true; };
    }

    public class RunResult
    {
        public bool Success;
        public string Status = "";
    }

    public static class Engine
    {
        public const int LvNormal = 0, LvOk = 1, LvWarn = 2, LvErr = 3, LvHead = 4, LvDim = 5;
        private const uint F_VALIDATE = 1, F_SAVE = 2, F_ENUM = 8;

        private static string T(string key, params object[] a) { return Strings.T(key, a); }

        private static void Step(Action<string, int> log, ref int n, int total, string key)
        {
            n++;
            log("", LvNormal);
            log("[" + n + "/" + total + "] " + T(key), LvHead);
        }

        private static RunResult Fail(RunResult r, Action<string, int> log, string key)
        {
            r.Status = T(key);
            log(r.Status, LvErr);
            return r;
        }

        private static int ApplyCfg(IPlatform p, NvConfig cfg, uint flags)
        {
            int st = p.NvSetConfig(cfg, flags | F_ENUM);
            if (st != 0) st = p.NvSetConfig(cfg, flags);
            return st;
        }

        private class Cand
        {
            public string Name; public uint Type; public bool Scaled; public bool Native;
            public Cand(string n, uint ty, bool sc) { Name = n; Type = ty; Scaled = sc; }
        }

        // Stretching: NVIDIA Control Panel creates a GPU-scaled custom resolution with the "Automatic" timing - the monitor keeps
        // receiving its native timing and the GPU stretches the (smaller) source mode. A custom timing of the same size as the
        // source would be sent to the monitor 1:1 and the monitor would decide how to scale it (usually with side bars).
        //
        // If the requested size is the monitor's own size (for example 2560x1440 on a 2560x1440 monitor), the driver's "Automatic"
        // timing has exactly that size: it is the timing the monitor itself reports. It is used as it is (native = true) and nothing
        // custom is created or saved. Replacing it with our own CVT-RB timing would send the monitor a signal it did not ask for.
        private static bool Create(IPlatform p, uint id, Timing t, bool stretch, bool allowCleanup, Action<string, int> log, out double realHz, out int lastSt, out bool scaled, out bool native)
        {
            realHz = 0; lastSt = 0; scaled = false; native = false;
            List<Cand> plan = new List<Cand>();
            if (stretch)
            {
                plan.Add(new Cand("Automatic", 1, true));
                plan.Add(new Cand("Current", 0, true));
                Cand own = new Cand("Automatic", 1, false);
                own.Native = true;
                plan.Add(own);
            }
            plan.Add(new Cand("CVT-RB", 6, false));
            plan.Add(new Cand("Automatic", 1, false));
            plan.Add(new Cand("CVT", 5, false));
            plan.Add(new Cand("GTF", 7, false));
            foreach (Cand c in plan)
            {
                int st = p.NvGetTiming(id, (uint)t.W, (uint)t.H, t.Hz, c.Type);
                if (st != 0) { log(T("att_gettiming_fail", c.Name, p.NvErr(st), st), LvWarn); lastSt = st; continue; }
                double[] pt = p.NvPendingTiming();
                double real = (pt[2] > 0 && pt[3] > 0) ? pt[4] * 10000.0 / (pt[2] * pt[3]) : 0.0;
                bool bigger = pt[0] >= t.W && pt[1] >= t.H && (pt[0] > t.W || pt[1] > t.H);
                bool exact = pt[0] == t.W && pt[1] == t.H;
                bool refreshOk = Math.Abs(real - t.Hz) / t.Hz <= 0.01;
                if (c.Scaled && !bigger) { if (!exact) log(T("att_not_native", c.Name, pt[0], pt[1], real), LvWarn); continue; }
                if (!c.Scaled && !exact) { log(T("att_mismatch", c.Name, pt[0], pt[1], real), LvWarn); continue; }
                if (!refreshOk) { log(T("att_mismatch", c.Name, pt[0], pt[1], real), LvWarn); continue; }
                if (c.Native)
                {
                    if (allowCleanup)
                    {
                        // an older run (or another tool) may have saved a 1:1 custom timing of this very size: the driver then offers it
                        // instead of the monitor's own one. Remove it and ask the driver again.
                        IModePlatform mp2 = p as IModePlatform;
                        int gone = 0;
                        if (mp2 != null)
                        {
                            try { gone = mp2.NvDeleteSameSize(id, (uint)t.W, (uint)t.H, t.Hz); } catch (Exception) { gone = 0; }
                        }
                        if (gone > 0)
                        {
                            log(T("stale_custom_removed", t.W, t.H, t.Hz), LvWarn);
                            p.Sleep(1500);
                            return Create(p, id, t, stretch, false, log, out realHz, out lastSt, out scaled, out native);
                        }
                    }
                    realHz = real; scaled = false; native = true;
                    log(T("att_native", c.Name, pt[0], pt[1], real), LvOk);
                    return true;
                }
                st = p.NvTry(id);
                if (st != 0) { log(T("att_try_fail", c.Name, p.NvErr(st), st), LvWarn); lastSt = st; continue; }
                realHz = real;
                scaled = c.Scaled;
                if (c.Scaled) log(T("att_scaled", c.Name, pt[0], pt[1], real, t.W, t.H), LvOk);
                else log(T("att_ok", c.Name, pt[0], pt[2], pt[1], pt[3], pt[4] / 100.0, real), LvOk);
                return true;
            }
            log(T("att_manual"), LvWarn);
            p.NvPrepareManual(t);
            int s2 = p.NvTry(id);
            if (s2 == 0) { realHz = t.RealHz; return true; }
            lastSt = s2;
            return false;
        }

        private static bool ModeMatches(int[] cur, Timing t, double realHz)
        {
            return cur != null && cur[0] == t.W && cur[1] == t.H && Math.Abs(cur[2] - realHz) <= 1.0;
        }

        // Every display that was on before must still be on afterwards. Windows 10/11 can switch a display off when monitor devices are
        // disabled and enabled, or when the display configuration changes. A display that went missing is switched on again at its old place.
        // Returns how many displays were missing.
        private static int KeepDisplays(IPlatform p, IDisplayPlatform dp, DisplaySnapshot snap, Action<string, int> log)
        {
            if (dp == null || snap == null || snap.Items.Count < 2) return 0;
            int lost = 0;
            try
            {
                List<DisplayState> miss = dp.DisplaysMissing(snap);
                if (miss.Count == 0) return 0;
                lost = miss.Count;
                foreach (DisplayState st in miss) log(T("disp_lost", st.Name, st.W, st.H), LvWarn);
                dp.DisplaysReattach(snap, miss);
                p.Sleep(2500);
                miss = dp.DisplaysMissing(snap);
                if (miss.Count > 0)
                {
                    dp.DisplaysExtend();
                    p.Sleep(3000);
                    miss = dp.DisplaysMissing(snap);
                }
                if (miss.Count == 0) log(T("disp_back"), LvOk);
                else { foreach (DisplayState st2 in miss) log(T("disp_still_off", st2.Name), LvErr); }
            }
            catch (Exception) { }
            return lost;
        }

        private static string ColorNames(int mask)
        {
            List<string> n = new List<string>();
            if ((mask & 1) != 0) n.Add(T("col_gamma"));
            if ((mask & 2) != 0) n.Add(T("col_dvc"));
            if ((mask & 4) != 0) n.Add(T("col_hue"));
            return string.Join(", ", n.ToArray());
        }

        // Puts back brightness/contrast/gamma, digital vibrance and hue if the driver reset them (it can do that after a mode change,
        // after the display configuration is applied and after the monitors come back). The driver sometimes resets them a moment
        // later, so the check is repeated. Returns the mask of what had to be put back (0 = nothing was reset).
        private static int KeepColors(IPlatform p, IColorPlatform cp, ColorSnapshot snap, Action<string, int> log)
        {
            if (cp == null || snap == null) return 0;
            int total = 0;
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    int m = cp.ColorRestore(snap);
                    if (m == 0) break;
                    total |= m;
                    p.Sleep(600);
                }
                int left = cp.ColorDiff(snap);
                if (left != 0) log(T("col_fail", ColorNames(left)), LvWarn);
                else if (total != 0) log(T("col_restored", ColorNames(total)), LvOk);
            }
            catch (Exception) { }
            return total;
        }

        public static RunResult Run(IPlatform p, Options o, Callbacks cb)
        {
            RunResult res = new RunResult();
            Action<string, int> log = cb.Log;
            bool disabledByUs = false, keepDisabled = false;
            int stepNo = 0;
            int stepTotal = 0;
            IColorPlatform cp = o.KeepColors ? (p as IColorPlatform) : null;
            IModePlatform mp = p as IModePlatform;
            IDisplayPlatform dp = p as IDisplayPlatform;
            IMonitorPlatform mtp = p as IMonitorPlatform;
            DisplaySnapshot screens = null;
            ColorSnapshot colors = null;
            int colorFixed = 0;
            try
            {
                if (!p.Is64Bit()) return Fail(res, log, "need64");
                DispInfo prim = null;
                List<DispInfo> disp = p.Displays();
                foreach (DispInfo d in disp) { if (d.Primary) { prim = d; break; } }
                if (prim == null) return Fail(res, log, "no_disp");
                if (prim.Desc == null || prim.Desc.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) < 0) return Fail(res, log, "primary_not_nvidia");

                Validation v = Cvt.Validate(o.Width, o.Height, o.Hz, o.Stretch);
                if (v.Errors.Count > 0)
                {
                    foreach (string e in v.Errors) log(e, LvErr);
                    res.Status = v.Errors[0];
                    return res;
                }
                Timing t = v.Timing;

                if (p.HasSavedMonitors()) { log(T("stale_state"), LvWarn); p.RestoreMonitors(log); p.Sleep(3000); }

                // the displays that are on now: all of them must still be on at the end
                if (dp != null) { try { screens = dp.DisplaysSave(); } catch (Exception) { screens = null; } }
                int nDisp = (screens != null) ? screens.Items.Count : disp.Count;
                int build = WinInfo.Build();
                log(T("env_info", build >= 22000 ? "11" : "10", build, nDisp), LvDim);

                p.NvInit();
                uint id = p.NvDisplayId(prim.Name);

                // brightness, contrast, gamma, digital vibrance, hue: saved before anything changes
                if (cp != null)
                {
                    try { colors = cp.ColorSave(prim.Name); } catch (Exception) { colors = null; }
                    if (colors != null && colors.Mask != 0) log(T("col_saved", ColorNames(colors.Mask)), LvDim);
                    else { colors = null; log(T("col_none"), LvWarn); }
                }

                // The picture goes to the monitor at its native resolution, so a refresh rate that the monitor does not list for that
                // resolution means an out-of-range signal (distorted picture, noise, artifacts). Checked before the monitors are disabled,
                // because with the monitor devices disabled Windows does not limit the list any more.
                if (o.Stretch && mp != null && !o.AllowOverRefresh)
                {
                    int nw = prim.W, nh = prim.H;
                    double[] sig0 = null;
                    try { sig0 = p.NvCurrentSignal(id, (uint)t.W, (uint)t.H, o.Hz); } catch (Exception) { sig0 = null; }
                    if (sig0 != null && sig0.Length >= 2 && sig0[0] > 0 && sig0[1] > 0) { nw = (int)sig0[0]; nh = (int)sig0[1]; }
                    int maxHz = 0;
                    try { maxHz = mp.MaxRefresh(prim.Name, nw, nh); } catch (Exception) { maxHz = 0; }
                    if (maxHz > 0 && o.Hz > maxHz + 1.0)
                    {
                        res.Status = T("hz_over_limit", maxHz, nw, nh, o.Hz);
                        log(res.Status, LvErr);
                        return res;
                    }
                }

                // the untouched configuration: used to build the stretched one and to roll back
                NvConfig orig = null;
                int primIdx = -1;
                if (o.Stretch)
                {
                    try { orig = p.NvGetConfig(); } catch (Exception) { orig = null; }
                    primIdx = Layout.FindPrimary(orig, id);
                    if (primIdx < 0) log(T("stretch_unavailable"), LvWarn);
                }
                // The monitor devices of ALL monitors are disabled (then the driver does not limit the modes to what the monitors report).
                // On Windows 10/11 that can drop a display from the desktop, so the displays are checked right afterwards and a dropped one
                // is switched back on. /primaryonly disables only the monitor device of the primary display.
                bool doDisable = o.DisableMonitors, onlyPrimary = false;
                if (doDisable && o.DisablePrimaryOnly && mtp != null) { onlyPrimary = true; log(T("mon_primary_only"), LvDim); }
                // disable + create + [stretch] + check + save
                stepTotal = (doDisable ? 1 : 0) + 1 + ((o.Stretch && primIdx >= 0) ? 1 : 0) + 2;

                if (doDisable)
                {
                    Step(log, ref stepNo, stepTotal, "step_disable");
                    disabledByUs = (onlyPrimary ? mtp.DisableMonitorFor(prim.Name, log) : p.DisableMonitors(log)) > 0;
                    p.Sleep(3000);
                    KeepDisplays(p, dp, screens, log);   // every display that was on must still be on
                }

                // ---- create the custom resolution (Try = the same as "Test" in NVIDIA Control Panel) ----
                Step(log, ref stepNo, stepTotal, "step_create");
                double realHz; int lastSt; bool scaledTiming, nativeMode;
                if (!Create(p, id, t, o.Stretch, true, log, out realHz, out lastSt, out scaledTiming, out nativeMode))
                {
                    res.Status = T("all_rejected", p.NvErr(lastSt), lastSt);
                    log("  " + res.Status, LvErr);
                    return res;
                }
                if (!nativeMode) log(T("accepted"), LvOk);

                // ---- stretch right away: GPU scaling to the native timing, other monitors keep their place ----
                bool saved = false, stretched = false;
                NvConfig applied = null;
                if (o.Stretch && primIdx >= 0)
                {
                    Step(log, ref stepNo, stepTotal, "step_stretch");
                    if (!nativeMode)
                    {
                        int st = p.NvSave(id, o.SaveOutputOnly, o.SaveMonitorOnly);   // the mode has to be in the driver's list first
                        if (st != 0)
                        {
                            res.Status = T("save_fail", p.NvErr(st), st);
                            log("  " + res.Status, LvErr);
                            p.NvRevert(id);
                            return res;
                        }
                        saved = true;
                    }
                    applied = Layout.BuildStretch(orig, primIdx, t.W, t.H, o.Hz);
                    int bad = p.NvSetConfig(applied, F_VALIDATE);
                    if (bad == 0) bad = ApplyCfg(p, applied, 0);
                    if (bad != 0)
                    {
                        log(T("stretch_cfg_fail", p.NvErr(bad), bad), LvWarn);
                        applied = null;
                    }
                    else
                    {
                        stretched = true;
                        log(T("stretch_applied", t.W, t.H), LvOk);
                    }
                }

                // ---- check ----
                Step(log, ref stepNo, stepTotal, "step_check");
                p.Sleep(3000);
                int[] cur = p.CurrentMode(prim.Name);
                if (ModeMatches(cur, t, realHz)) log(T("mode_match", cur[0], cur[1], cur[2]), LvOk);
                else if (cur != null) log(T("mode_diff", cur[0], cur[1], cur[2], t.W, t.H, realHz), LvWarn);
                if (o.Stretch)
                {
                    double[] sig = null;
                    try { sig = p.NvCurrentSignal(id, (uint)t.W, (uint)t.H, o.Hz); } catch (Exception) { sig = null; }
                    if (sig != null && sig.Length >= 2)
                    {
                        if (sig[0] > t.W || sig[1] > t.H) log(T("signal_native", sig[0], sig[1]), LvOk);
                        else log(T("signal_1to1", sig[0], sig[1]), LvWarn);
                    }
                }
                if (stretched)
                {
                    NvConfig now = null;
                    try { now = p.NvGetConfig(); } catch (Exception) { now = null; }
                    int ni = Layout.FindPrimary(now, id);
                    if (ni >= 0 && now.Paths[ni].Targets.Count > 0)
                    {
                        uint sc = now.Paths[ni].Targets[0].Adv.scaling;
                        log(T("stretch_state", now.Paths[ni].Src.width, now.Paths[ni].Src.height, Layout.ScalingName(sc)), LvDim);
                        if (sc != Layout.ScalingFullScreenGpu && sc != 1) log(T("stretch_inactive", Layout.ScalingName(sc)), LvWarn);
                    }
                }

                KeepDisplays(p, dp, screens, log);                 // all displays on, as before
                colorFixed |= KeepColors(p, cp, colors, log);      // so that the picture shown for the test already has your colors

                bool keep = cb.ConfirmKeep(T("keep_question"), o.TestSeconds);
                if (!keep)
                {
                    p.NvRevert(id);
                    if (applied != null && orig != null) { ApplyCfg(p, orig, 0); log(T("rollback_note"), LvWarn); }
                    if (saved) p.NvDeleteCustom(id);
                    if (colors != null || screens != null) { p.Sleep(1500); KeepDisplays(p, dp, screens, log); KeepColors(p, cp, colors, log); }
                    res.Status = T("reverted");
                    log("  " + res.Status, LvWarn);
                    return res;
                }

                // ---- save ----
                Step(log, ref stepNo, stepTotal, "step_save");
                if (!saved && !nativeMode)
                {
                    int st = p.NvSave(id, o.SaveOutputOnly, o.SaveMonitorOnly);
                    if (st != 0)
                    {
                        res.Status = T("save_fail", p.NvErr(st), st);
                        log("  " + res.Status, LvErr);
                        p.NvRevert(id);
                        return res;
                    }
                    saved = true;
                }
                if (!nativeMode)
                {
                    if (p.NvHasCustom(id, (uint)t.W, (uint)t.H, scaledTiming ? -1.0 : t.Hz)) log(T("saved_listed"), LvOk);
                    else log(T("saved_notlisted"), LvWarn);
                }

                int winRc = 0;
                if (stretched)
                {
                    int st = ApplyCfg(p, applied, F_SAVE);                 // persist scaling, size and positions
                    if (st != 0) { log(T("stretch_cfg_fail", p.NvErr(st), st), LvWarn); }
                    NvConfig now = null;
                    try { now = p.NvGetConfig(); } catch (Exception) { now = null; }
                    if (now != null && !Layout.Matches(now, applied))
                    {
                        ApplyCfg(p, applied, F_SAVE);
                        log(T("layout_fixed"), LvWarn);
                    }
                }
                else
                {
                    winRc = p.SetWindowsMode(prim.Name, t.W, t.H, (int)Math.Round(realHz), true);
                }

                if (colors != null || screens != null) { p.Sleep(1500); KeepDisplays(p, dp, screens, log); colorFixed |= KeepColors(p, cp, colors, log); }

                if (winRc != 0)
                {
                    res.Status = T("win_apply_fail", winRc);
                    log("  " + res.Status, LvWarn);
                }
                else
                {
                    res.Status = stretched ? (nativeMode ? T("done_native") : T("done_stretch")) : (nativeMode ? T("done_native") : T("done"));
                    res.Success = true;
                    log("  " + res.Status, LvOk);
                }

                // ---- monitors back on ----
                if (disabledByUs)
                {
                    bool re = o.AskReenable ? cb.AskYesNo(T("reenable_prompt")) : o.ReenableMonitors;
                    if (!re)
                    {
                        keepDisabled = true;
                        log(T("keep_disabled", o.RestoreHint), LvWarn);
                        log(T("keep_disabled_warn"), LvWarn);
                    }
                    else
                    {
                        p.RestoreMonitors(log);
                        disabledByUs = false;
                        p.Sleep(4000);
                        bool ok;
                        if (stretched)
                        {
                            NvConfig after = null;
                            try { after = p.NvGetConfig(); } catch (Exception) { after = null; }
                            ok = Layout.Matches(after, applied);
                            if (!ok) { log(T("persist_lost"), LvWarn); ApplyCfg(p, applied, F_SAVE); }
                        }
                        else
                        {
                            int[] c2 = p.CurrentMode(prim.Name);
                            ok = ModeMatches(c2, t, realHz);
                            if (!ok) { log(T("persist_lost"), LvWarn); p.SetWindowsMode(prim.Name, t.W, t.H, (int)Math.Round(realHz), true); }
                        }
                        if (ok) log(T("persist_ok", t.W, t.H), LvOk);
                        KeepDisplays(p, dp, screens, log);
                        colorFixed |= KeepColors(p, cp, colors, log);
                    }
                }
                if (colors != null && colorFixed == 0) log(T("col_ok"), LvOk);
                return res;
            }
            catch (Exception ex)
            {
                res.Status = T("err_generic", ex.Message);
                log(T("fatal", ex.Message), LvErr);
                return res;
            }
            finally
            {
                if (disabledByUs && !keepDisabled)
                {
                    try { p.RestoreMonitors(log); } catch (Exception) { }
                    if (colors != null || screens != null) { try { p.Sleep(4000); KeepDisplays(p, dp, screens, log); KeepColors(p, cp, colors, log); } catch (Exception) { } }
                }
            }
        }
    }
}
'@
Add-Type -TypeDefinition $core -Language CSharp

function T([string]$key, [object[]]$a = @()) { return [CustomRes.Strings]::T($key, $a) }
function Say($t, $c = 'Gray') { Write-Host $t -ForegroundColor $c }
$LevelColors = @('Gray', 'Green', 'Yellow', 'Red', 'Cyan', 'DarkGray')

# confirmation with a timer: Y/Enter = keep, N/Esc/timeout = revert (physical keys, so the keyboard layout does not matter)
function Ask-Keep([int]$sec) {
    $end = (Get-Date).AddSeconds($sec)
    while ((Get-Date) -lt $end) {
        if ([Console]::KeyAvailable) {
            $k = [Console]::ReadKey($true)
            if ($k.Key -eq 'Y' -or $k.Key -eq 'Enter') { Write-Host ''; return $true }
            if ($k.Key -eq 'N' -or $k.Key -eq 'Escape') { Write-Host ''; return $false }
        }
        $left = [int][math]::Ceiling(($end - (Get-Date)).TotalSeconds)
        Write-Host -NoNewline ("`r" + (T 'keep_prompt' @($left)))
        Start-Sleep -Milliseconds 200
    }
    Write-Host ''
    return $false
}

$logAction = [Action[string, int]] { param($text, $level) Write-Host $text -ForegroundColor $LevelColors[$level] }

#--MAIN--
# ---- arguments and language ----
$argLine = "$env:CR_ARG"
$isRestore = $argLine -match '(^|\s)[/-]restore(\s|$)'
$noDisable = $argLine -match '(^|\s)[/-]nodisable(\s|$)'
$noColors = $argLine -match '(^|\s)[/-]nocolors?(\s|$)'
$force = $argLine -match '(^|\s)[/-]force(\s|$)'
$primaryOnly = $argLine -match '(^|\s)[/-]primaryonly(\s|$)'
if ($argLine -match '(^|\s)[/-]nostretch(\s|$)') { $Stretch = $false }
if ($argLine -match '(^|\s)[/-]ru(\s|$)') { [CustomRes.Strings]::Lang = 'ru' }
elseif ($argLine -match '(^|\s)[/-]en(\s|$)') { [CustomRes.Strings]::Lang = 'en' }
elseif (-not $isRestore) {
    $ans = Read-Host 'Language / Язык:  1 = English,  2 = Русский  [Enter = English]'
    if ($ans.Trim() -match '^(2|r|ru|р|рус|русский)$') { [CustomRes.Strings]::Lang = 'ru' }
}

if ($isRestore) {
    Say (T 'restoring') 'Cyan'
    [CustomRes.Monitors]::RestoreAll($logAction)
    return
}

$platform = $(if ($global:TestPlatform) { $global:TestPlatform } else { New-Object CustomRes.WinPlatform })

try {
    $primary = @($platform.Displays() | Where-Object { $_.Primary })[0]
    if ($null -eq $primary) { Say (T 'no_disp') 'Red'; return }
    if ($primary.Desc -notmatch 'NVIDIA') { Say (T 'primary_not_nvidia') 'Red'; return }
    Say (T 'primary_info' @($primary.Name, $primary.Desc, $primary.W, $primary.H, $primary.Hz)) 'DarkGray'
    Say (T 'only_primary') 'DarkGray'
    Write-Host ''

    # ---- enter and validate the resolution ----
    $rw = 0; $rh = 0; $rhz = 0.0; $timing = $null
    while ($true) {
        $raw = Read-Host (T 'res_prompt')
        if ([string]::IsNullOrWhiteSpace($raw)) { Say (T 'cancelled') 'Yellow'; return }
        if (-not [CustomRes.Cvt]::TryParse($raw, [ref]$rw, [ref]$rh, [ref]$rhz)) { Say (T 'bad_format') 'Yellow'; continue }
        if ($rhz -le 0) {
            $answer = Read-Host (T 'hz_prompt' @($primary.Hz))
            $hzText = $(if ([string]::IsNullOrWhiteSpace($answer)) { "$($primary.Hz)" } else { $answer })
            $parsed = 0.0
            if (-not [CustomRes.Cvt]::TryParseHz($hzText, [ref]$parsed)) { Say (T 'hz_bad') 'Yellow'; continue }
            $rhz = $parsed
        }
        $check = [CustomRes.Cvt]::Validate($rw, $rh, $rhz, [bool]$Stretch)
        if ($check.Errors.Count -gt 0) {
            foreach ($msg in $check.Errors) { Say ("  {0}: {1}" -f (T 'lbl_error'), $msg) 'Red' }
            continue
        }
        foreach ($msg in $check.Warnings) { Say ("  {0}: {1}" -f (T 'lbl_note'), $msg) 'Yellow' }
        if ($check.Caveats.Count -gt 0) {
            foreach ($msg in $check.Caveats) { Say ("  {0}: {1}" -f (T 'lbl_warning'), $msg) 'Yellow' }
            $confirm = Read-Host (T 'confirm_caveat')
            if ($confirm.Trim() -notmatch '^(y|yes|д|да|н)$') { continue }
        }
        $timing = $check.Timing
        if ($Stretch) {
            Say (T 'sum_res' @($timing.W, $timing.H, $rhz)) 'Green'
            Say (T 'sum_stretch' @($timing.W, $timing.H)) 'Green'
        } else {
            Say (T 'sum_res' @($timing.W, $timing.H, $timing.RealHz)) 'Green'
            Say (T 'sum_timing' @($timing.W, $timing.HTotal, $timing.H, $timing.VTotal, $timing.PclkMHz, $timing.HFreqKHz)) 'Green'
        }
        Say (T 'timing_ok') 'Green'
        $go = Read-Host (T 'go_prompt')
        if ($go -match '^[nNтТ]') { continue }
        break
    }

    # ---- run the flow ----
    $opt = New-Object CustomRes.Options
    $opt.Width = $rw; $opt.Height = $rh; $opt.Hz = $rhz
    $opt.Stretch = $Stretch
    $opt.DisableMonitors = -not $noDisable
    $opt.KeepColors = -not $noColors
    $opt.AllowOverRefresh = [bool]$force
    $opt.DisablePrimaryOnly = [bool]$primaryOnly
    $opt.AskReenable = $true
    $opt.RestoreHint = ([IO.Path]::GetFileName($env:CR_SELF) + ' /restore')
    $opt.TestSeconds = $TestSeconds

    $callbacks = New-Object CustomRes.Callbacks
    $callbacks.Log = $logAction
    $callbacks.ConfirmKeep = [Func[string, int, bool]] { param($question, $seconds) return [bool](Ask-Keep $seconds) }
    $callbacks.AskYesNo = [Func[string, bool]] { param($question) $reply = Read-Host $question; return [bool](-not ($reply -match '^[nNтТ]')) }

    $result = [CustomRes.Engine]::Run($platform, $opt, $callbacks)
    Write-Host ''
    Say (T 'summary_hdr') 'Cyan'
    Say ("  " + $result.Status) $(if ($result.Success) { 'Green' } else { 'Yellow' })
}
catch {
    Write-Host ''
    Say (T 'fatal' @($_.Exception.Message)) 'Red'
}