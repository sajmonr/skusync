/** A link out to a SkuLabs item. */
export function SkulabsLink({ url, label }: { url: string; label: string }) {
  return (
    <s-link href={url} target="_blank">
      {label}
    </s-link>
  );
}
