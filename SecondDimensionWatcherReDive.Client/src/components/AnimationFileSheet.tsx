import { FileBrowser } from "./FileBrowser";
import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetHeader,
  SheetTitle,
} from "./ui/Sheet";

export default function AnimationFileSheet({
  title,
  animationId,
  onOpenChange,
}: {
  title: string;
  animationId: string;
  onOpenChange: (open: boolean) => void;
}) {
  return (
    <Sheet open onOpenChange={onOpenChange}>
      <SheetContent>
        <SheetHeader>
          <SheetTitle>{title}</SheetTitle>
        </SheetHeader>
        <SheetBody>
          <FileBrowser animationId={animationId} />
        </SheetBody>
      </SheetContent>
    </Sheet>
  );
}
