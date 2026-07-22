import { buildProductVariantQuery } from './build-product-variant-query';

describe('buildProductVariantQuery', () => {
  it('should translate table state into a paged, searchable Gridify query', () => {
    const query = buildProductVariantQuery({
      first: 25,
      rows: 25,
      search: 'shirt, blue',
      sortField: 'failedSyncAttempts',
      sortOrder: 1,
    });

    expect(query).toEqual({
      page: 2,
      pageSize: 25,
      filter: '(displayName=*shirt\\, blue/i|sku=*shirt\\, blue/i|barcode=*shirt\\, blue/i)',
      orderBy: 'failedSyncAttempts, id',
    });
  });

  it('should use the default ordering when the field is not allowlisted', () => {
    const query = buildProductVariantQuery({
      first: 0,
      rows: 10,
      search: '',
      sortField: 'FailedShopifySyncAttempts',
      sortOrder: 1,
    });

    expect(query.orderBy).toBe('updatedOnUtc, id');
  });
});
