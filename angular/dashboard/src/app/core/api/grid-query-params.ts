import { HttpParams } from '@angular/common/http';
import { IGridifyQuery } from 'gridify-client';

export function toGridQueryParams(query: IGridifyQuery): HttpParams {
  let parameters = new HttpParams()
    .set('page', query.page)
    .set('pageSize', query.pageSize);

  if (query.filter) {
    parameters = parameters.set('filter', query.filter);
  }

  if (query.orderBy) {
    parameters = parameters.set('orderBy', query.orderBy);
  }

  return parameters;
}
