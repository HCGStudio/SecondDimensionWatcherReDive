import { useEffect, useRef, useState } from "react";

export function useTypewriter(
  targetText: string,
  isActive: boolean,
  charsPerTick = 1,
): string {
  const [displayedText, setDisplayedText] = useState("");
  const targetRef = useRef(targetText);
  targetRef.current = targetText;

  // Snap to full text when streaming finishes
  useEffect(() => {
    if (!isActive) {
      setDisplayedText(targetRef.current);
    }
  }, [isActive]);

  // Reset when target is cleared (new streaming session)
  useEffect(() => {
    if (targetText === "") {
      setDisplayedText("");
    }
  }, [targetText]);

  // Typewriter animation loop
  useEffect(() => {
    if (!isActive) return;

    const id = setInterval(() => {
      setDisplayedText((prev) => {
        const target = targetRef.current;
        if (prev.length >= target.length) return prev;

        // Speed up if buffer is large to prevent excessive lag
        const remaining = target.length - prev.length;
        const step =
          remaining > 50 ? Math.min(remaining, charsPerTick * 3) : charsPerTick;

        return target.slice(0, prev.length + step);
      });
    }, 20);

    return () => clearInterval(id);
  }, [isActive, charsPerTick]);

  return displayedText;
}
