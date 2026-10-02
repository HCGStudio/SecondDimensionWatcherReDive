import i18n from "./index";
import enAuth from "./locales/en/auth.json";
import jaAuth from "./locales/ja/auth.json";
import zhCnAuth from "./locales/zh-CN/auth.json";

i18n.addResourceBundle("en", "auth", enAuth, true, true);
i18n.addResourceBundle("ja", "auth", jaAuth, true, true);
i18n.addResourceBundle("zh-cn", "auth", zhCnAuth, true, true);
