import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as apiModule from '../api';
import { api } from '../api';
import { AuthProvider } from '../auth';
import { ErrorBox, SlaBadge, StatusBadge, TicketTable } from '../components/bits';
import { notificationText } from '../components/Layout';
import { LanguageProvider, LanguageSwitch, UiError, detectLanguage, errorText, translate } from '../i18n';
import { dictionaries, en, vi as viTexts } from '../locales';
import Login from '../pages/Login';

// every test starts from a clean browser state
beforeEach(() => {
  vi.restoreAllMocks();
  localStorage.clear();
  document.documentElement.lang = '';
});

const placeholders = (text) => [...text.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort();

describe('the dictionaries', () => {
  it('have exactly the same keys, so nothing is left in English by accident', () => {
    expect(Object.keys(viTexts).sort()).toEqual(Object.keys(en).sort());
  });

  it('keep the placeholders of every English text', () => {
    for (const key of Object.keys(en)) {
      expect(placeholders(viTexts[key]), key).toEqual(placeholders(en[key]));
    }
  });

  it('are translated: only a few texts are the same in both languages', () => {
    const same = Object.keys(en).filter((key) => viTexts[key] === en[key]).sort();
    // "{name}: {problem}" is all placeholders; "agent" is the same word in both languages
    expect(same).toEqual(['attach.named', 'login.role.agent', 'user.agent']);
  });

  it('cover every status, priority, action and event the server can send', () => {
    for (const key of [1, 2, 3, 4, 5, 6].map((n) => `status.${n}`)) expect(viTexts[key], key).toBeTruthy();
    for (const key of [1, 2, 3, 4].map((n) => `priority.${n}`)) expect(viTexts[key], key).toBeTruthy();
    for (const key of [3, 4, 5, 6, 7, 8, 9, 10, 11, 12].map((n) => `action.${n}`)) expect(viTexts[key], key).toBeTruthy();
    for (const key of [6, 8, 10, 11, 12].map((n) => `prompt.${n}`)) expect(viTexts[key], key).toBeTruthy();
    for (const key of [1, 2, 3, 4, 5, 6].map((n) => `event.${n}`)) expect(viTexts[key], key).toBeTruthy();
  });
});

describe('translate', () => {
  it('fills placeholders and leaves unknown ones visible', () => {
    expect(translate('en', 'pager.page', { page: 2, pages: 5 })).toBe('Page 2 of 5');
    expect(translate('vi', 'pager.page', { page: 2, pages: 5 })).toBe('Trang 2 / 5');
    expect(translate('en', 'pager.page', { page: 2 })).toBe('Page 2 of {pages}');
  });

  it('falls back to English, then to the key', () => {
    dictionaries.vi.__only_english = undefined;
    dictionaries.en.__only_english = 'only here';
    expect(translate('vi', '__only_english')).toBe('only here');
    delete dictionaries.en.__only_english;
    delete dictionaries.vi.__only_english;
    expect(translate('vi', 'no.such.key')).toBe('no.such.key');
  });

  it('chooses the singular or plural wording when given a count', () => {
    expect(translate('en', 'new.imagesFailed', { count: 1, list: 'a.png (x)' })).toContain('one image could not');
    expect(translate('en', 'new.imagesFailed', { count: 3, list: 'a.png (x)' })).toContain('3 images could not');
    expect(translate('vi', 'new.imagesFailed', { count: 3, list: 'a.png (x)' })).toContain('3 ảnh');
  });
});

describe('detectLanguage', () => {
  it('prefers what the person chose, then the browser, then English', () => {
    localStorage.setItem('helpdesk.lang', 'vi');
    expect(detectLanguage()).toBe('vi');

    localStorage.setItem('helpdesk.lang', 'klingon'); // a stale or invented value is ignored
    expect(detectLanguage()).toBe('en');

    localStorage.clear();
    setBrowserLanguage('vi-VN');
    expect(detectLanguage()).toBe('vi');
    setBrowserLanguage('fr-FR');
    expect(detectLanguage()).toBe('en');
    setBrowserLanguage('en-US');
  });
});

function setBrowserLanguage(language) {
  Object.defineProperty(window.navigator, 'language', { value: language, configurable: true });
}

describe('errorText', () => {
  const t = (key) => translate('vi', key);

  it('words our own errors by key or kind, and shows the server message as it came', () => {
    expect(errorText(t, new UiError('attach.empty'))).toBe('Tệp trống.');
    expect(errorText(t, Object.assign(new Error('Cannot reach the server.'), { kind: 'network' }))).toContain('Không kết nối được');
    expect(errorText(t, new Error('Phiếu đang ở trạng thái Closed.'))).toBe('Phiếu đang ở trạng thái Closed.');
    expect(errorText(t, new Error(''))).toBe('Yêu cầu không thành công');
  });
});

describe('what the language switch changes', () => {
  it('re-words the page, remembers the choice, and tells the API', async () => {
    const tell = vi.spyOn(apiModule, 'setLanguage');
    render(<MemoryRouter><AuthProvider><LanguageProvider initial="en"><Login /></LanguageProvider></AuthProvider></MemoryRouter>);
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument();

    await userEvent.selectOptions(screen.getByLabelText('Language'), 'vi');

    expect(screen.getByRole('button', { name: 'Đăng nhập' })).toBeInTheDocument();
    expect(screen.getByLabelText('Tên đăng nhập')).toBeInTheDocument();
    expect(screen.getByLabelText('Ngôn ngữ')).toHaveValue('vi');
    expect(localStorage.getItem('helpdesk.lang')).toBe('vi');
    expect(document.documentElement.lang).toBe('vi');
    expect(tell).toHaveBeenLastCalledWith('vi');
  });

  it('shows statuses, SLA and table headings in Vietnamese', () => {
    const ticket = {
      id: 't1', code: 'HD-1', title: 'VPN', status: 2, priority: 4, categoryName: 'IT support', requester: { name: 'alice' },
      assignee: null, creationTime: '2026-11-02T08:00:00Z', waitingSince: '2026-11-02T09:00:00Z',
    };
    render(
      <MemoryRouter>
        <LanguageProvider initial="vi">
          <StatusBadge status={2} />
          <SlaBadge ticket={ticket} />
          <TicketTable tickets={[ticket]} showRequester />
        </LanguageProvider>
      </MemoryRouter>
    );

    expect(screen.getAllByText('Đang xử lý').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Tạm dừng SLA').length).toBeGreaterThan(0);
    expect(screen.getByText('Khẩn cấp')).toBeInTheDocument();
    expect(screen.getByText('Người yêu cầu')).toBeInTheDocument();
  });

  it('puts the chosen language on every API call', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('{}', { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    apiModule.setLanguage('vi');

    await api.ticket('t1');

    expect(fetchMock.mock.calls[0][1].headers['Accept-Language']).toBe('vi');
    apiModule.setLanguage('en');
    vi.unstubAllGlobals();
  });
});

describe('notifications are worded here, not by the server', () => {
  const item = (extra) => ({ code: 'HD-7', message: 'HD-7 was assigned and is now Open', kind: 'Assigned', status: 'Open', priority: 'High', ...extra });
  const tEn = (key, params) => translate('en', key, params);
  const tVi = (key, params) => translate('vi', key, params);

  it.each([
    [{ kind: 'Created' }, 'New ticket HD-7', 'Phiếu mới HD-7'],
    [{ kind: 'Assigned' }, 'HD-7 was assigned and is now Open', 'HD-7 đã được giao và hiện ở trạng thái Đang xử lý'],
    [{ kind: 'PriorityChanged' }, 'HD-7 is now High priority', 'HD-7 có mức ưu tiên Cao'],
    [{ kind: 'StatusChanged', status: 'Resolved' }, 'HD-7 is now Resolved', 'HD-7 hiện ở trạng thái Đã giải quyết'],
    [{ kind: 'Reopened' }, 'HD-7 was reopened', 'HD-7 đã được mở lại'],
    [{ kind: 'AutoClosed' }, 'HD-7 was closed automatically', 'HD-7 đã tự động đóng'],
  ])('%j', (extra, english, vietnamese) => {
    expect(notificationText(tEn, item(extra))).toBe(english);
    expect(notificationText(tVi, item(extra))).toBe(vietnamese);
  });

  it('falls back to the server text for a kind it does not know', () => {
    expect(notificationText(tVi, item({ kind: 'Brand new', message: 'something happened' }))).toBe('something happened');
  });
});

describe('ErrorBox', () => {
  it('words a client-side error in the chosen language', () => {
    render(<LanguageProvider initial="vi"><ErrorBox error={Object.assign(new Error('x'), { kind: 'expired' })} /></LanguageProvider>);
    expect(screen.getByRole('alert')).toHaveTextContent('Phiên đăng nhập đã hết hạn');
  });
});

describe('LanguageSwitch', () => {
  it('works without a provider, in English', async () => {
    render(<LanguageSwitch />);
    await waitFor(() => expect(screen.getByLabelText('Language')).toBeInTheDocument());
  });
});
