import logo from "@/assets/capitec-bank-logo.jpg";

/**
 * The logo on a white tile (the supplied JPG has a white background, so it always sits on
 * white). Decorative: it renders beside the portal name inside the home link, so the link's
 * accessible name stays the text.
 */
export function BrandMark() {
  return (
    <span className="inline-grid place-items-center rounded-xl bg-white px-2 py-1.5 ring-1 ring-black/5">
      <img src={logo} alt="" width={40} height={21} className="h-auto w-10" />
    </span>
  );
}
