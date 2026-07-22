import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PageHeader } from './page-header';

describe('PageHeader', () => {
  let fixture: ComponentFixture<PageHeader>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [PageHeader] }).compileComponents();
    fixture = TestBed.createComponent(PageHeader);
  });

  it('should render its heading content', () => {
    fixture.componentRef.setInput('eyebrow', 'Administration');
    fixture.componentRef.setInput('title', 'Product variants');
    fixture.componentRef.setInput('description', 'Inspect synchronized product data.');
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('p')?.textContent).toContain('Administration');
    expect(element.querySelector('h2')?.textContent).toContain('Product variants');
    expect(element.querySelector('.page-heading > span')?.textContent).toContain(
      'Inspect synchronized product data.'
    );
  });
});
