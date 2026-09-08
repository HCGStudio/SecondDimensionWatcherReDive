import React from "react";

import { cn } from "../lib/cn";

const brandIconUrl = new URL("../favicon.svg", import.meta.url).href;

export const BrandIcon: React.FC<{ className?: string }> = ({ className }) => (
  <img
    src={brandIconUrl}
    alt=""
    aria-hidden="true"
    width={40}
    height={40}
    className={cn("shrink-0", className)}
  />
);
