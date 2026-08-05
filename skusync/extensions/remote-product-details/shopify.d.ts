import '@shopify/ui-extensions';

//@ts-ignore
declare module './src/BlockExtension.tsx' {
  const shopify: import('@shopify/ui-extensions/admin.product-variant-details.block.render').Api;
  const globalThis: { shopify: typeof shopify };
}

//@ts-ignore
declare module './src/components/Failure.tsx' {
  const shopify: import('@shopify/ui-extensions/admin.product-variant-details.block.render').Api;
  const globalThis: { shopify: typeof shopify };
}

//@ts-ignore
declare module './src/components/Loading.tsx' {
  const shopify: import('@shopify/ui-extensions/admin.product-variant-details.block.render').Api;
  const globalThis: { shopify: typeof shopify };
}

//@ts-ignore
declare module './src/components/SkulabsLink.tsx' {
  const shopify: import('@shopify/ui-extensions/admin.product-variant-details.block.render').Api;
  const globalThis: { shopify: typeof shopify };
}

//@ts-ignore
declare module './src/hooks/useVariantInformation.ts' {
  const shopify: import('@shopify/ui-extensions/admin.product-variant-details.block.render').Api;
  const globalThis: { shopify: typeof shopify };
}

//@ts-ignore
declare module './src/api.ts' {
  const shopify: import('@shopify/ui-extensions/admin.product-variant-details.block.render').Api;
  const globalThis: { shopify: typeof shopify };
}

//@ts-ignore
declare module './src/config.ts' {
  const shopify: import('@shopify/ui-extensions/admin.product-variant-details.block.render').Api;
  const globalThis: { shopify: typeof shopify };
}
