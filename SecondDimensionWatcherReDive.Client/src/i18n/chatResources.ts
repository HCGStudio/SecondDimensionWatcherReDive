import i18n from "./index";
import enChat from "./locales/en/chat.json";
import jaChat from "./locales/ja/chat.json";
import zhCnChat from "./locales/zh-CN/chat.json";

i18n.addResourceBundle("en", "chat", enChat, true, true);
i18n.addResourceBundle("ja", "chat", jaChat, true, true);
i18n.addResourceBundle("zh-cn", "chat", zhCnChat, true, true);
