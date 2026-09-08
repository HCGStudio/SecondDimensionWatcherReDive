import i18n from "./index";
import enMetadataReview from "./locales/en/metadataReview.json";
import jaMetadataReview from "./locales/ja/metadataReview.json";
import zhCnMetadataReview from "./locales/zh-CN/metadataReview.json";

i18n.addResourceBundle("en", "metadataReview", enMetadataReview, true, true);
i18n.addResourceBundle("ja", "metadataReview", jaMetadataReview, true, true);
i18n.addResourceBundle(
  "zh-cn",
  "metadataReview",
  zhCnMetadataReview,
  true,
  true,
);
