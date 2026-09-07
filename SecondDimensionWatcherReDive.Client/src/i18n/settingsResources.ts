import i18n from "./index";
import enSettings from "./locales/en/settings.json";
import jaSettings from "./locales/ja/settings.json";
import zhCnSettings from "./locales/zh-CN/settings.json";

i18n.addResourceBundle("en", "settings", enSettings, true, true);
i18n.addResourceBundle("ja", "settings", jaSettings, true, true);
i18n.addResourceBundle("zh-cn", "settings", zhCnSettings, true, true);
