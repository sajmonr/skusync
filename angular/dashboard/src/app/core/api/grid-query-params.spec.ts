import { ConditionalOperator, GridifyQueryBuilder } from 'gridify-client';
import { toGridQueryParams } from './grid-query-params';

describe('toGridQueryParams', () => {
  it('should serialize a Gridify client query for the API', () => {
    const query = new GridifyQueryBuilder()
      .setPage(2)
      .setPageSize(50)
      .addCondition('failedSyncAttempts', ConditionalOperator.GreaterThan, 2)
      .addOrderBy('updatedOn', true)
      .build();

    const parameters = toGridQueryParams(query);

    expect(parameters.get('page')).toBe('2');
    expect(parameters.get('pageSize')).toBe('50');
    expect(parameters.get('filter')).toBe('failedSyncAttempts>2');
    expect(parameters.get('orderBy')).toBe('updatedOn desc');
  });
});
