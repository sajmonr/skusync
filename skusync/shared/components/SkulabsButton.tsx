/**
 * Opens a SkuLabs item in a new tab. `s-button` takes an `href` and behaves as a link, so this is a
 * navigation rather than an action — no click handler, and the middle-click and open-in-new-tab
 * behaviour merchants expect from a link still works.
 */
export function SkulabsButton({ url, label }: { url: string; label: string }) {
  return (
    <s-button href={url} target="_blank" variant="secondary">
      {label}
    </s-button>
  );
}
