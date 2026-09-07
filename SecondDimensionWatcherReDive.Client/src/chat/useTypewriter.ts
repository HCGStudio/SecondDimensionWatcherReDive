import { useEffect, useRef, useState } from "react";

export function useTypewriter(
  targetText: string,
  isActive: boolean,
  charsPerTick = 1,
  sequenceKey: string | number = 0,
): string {
  const [displayed, setDisplayed] = useState({ key: sequenceKey, text: "" });
  const [prefersReducedMotion, setPrefersReducedMotion] = useState(() =>
    typeof window === "undefined"
      ? false
      : window.matchMedia("(prefers-reduced-motion: reduce)").matches,
  );
  const targetRef = useRef(targetText);
  targetRef.current = targetText;

  useEffect(() => {
    const media = window.matchMedia("(prefers-reduced-motion: reduce)");
    const updatePreference = () => setPrefersReducedMotion(media.matches);
    updatePreference();
    media.addEventListener("change", updatePreference);
    return () => media.removeEventListener("change", updatePreference);
  }, []);

  // Snap to full text when streaming finishes or motion is reduced.
  useEffect(() => {
    if (!isActive || prefersReducedMotion) {
      setDisplayed({ key: sequenceKey, text: targetText });
    }
  }, [isActive, prefersReducedMotion, sequenceKey, targetText]);

  // Reset when target is cleared (new streaming session)
  useEffect(() => {
    if (targetText === "") {
      setDisplayed({ key: sequenceKey, text: "" });
    }
  }, [sequenceKey, targetText]);

  // Typewriter animation loop
  useEffect(() => {
    if (!isActive || prefersReducedMotion) return;

    const id = setInterval(() => {
      setDisplayed((current) => {
        const previousText = current.key === sequenceKey ? current.text : "";
        const target = targetRef.current;
        if (previousText.length >= target.length) return current;

        // Speed up if buffer is large to prevent excessive lag
        const remaining = target.length - previousText.length;
        const step =
          remaining > 50 ? Math.min(remaining, charsPerTick * 3) : charsPerTick;

        return {
          key: sequenceKey,
          text: target.slice(0, previousText.length + step),
        };
      });
    }, 20);

    return () => clearInterval(id);
  }, [isActive, charsPerTick, prefersReducedMotion, sequenceKey]);

  if (displayed.key !== sequenceKey) {
    return prefersReducedMotion ? targetText : "";
  }
  return displayed.text;
}
