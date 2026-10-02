import React, { useMemo } from "react";

import { cn } from "../lib/cn";
import { useTheme } from "../theme/ThemeProvider";
import { getBrandIconUrl } from "../theme/brandIcon";

export const BrandIcon: React.FC<{ className?: string }> = ({ className }) => {
  const { resolvedMode, lightTheme, darkTheme } = useTheme();
  const theme = resolvedMode === "dark" ? darkTheme : lightTheme;
  const src = useMemo(
    () => getBrandIconUrl(theme, resolvedMode),
    [theme, resolvedMode],
  );

  return (
    <img
      src={src}
      alt=""
      aria-hidden="true"
      width={40}
      height={40}
      className={cn("shrink-0", className)}
    />
  );
};
