/** A spinner with the label that says what is being looked up. */
export function Loading({ message }: { message: string }) {
  return (
    <s-stack direction="inline" gap="small" alignItems="center">
      <s-spinner size="base" accessibilityLabel={message} />
      <s-text>{message}</s-text>
    </s-stack>
  );
}
