import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { LanguageProvider } from '../i18n';
import { ThemeSwitch, applyTheme, detectTheme } from '../theme';

const systemPrefersDark = (dark) => {
  window.matchMedia = vi.fn().mockReturnValue({ matches: dark });
};

beforeEach(() => {
  localStorage.clear();
  delete document.documentElement.dataset.theme;
  systemPrefersDark(false);
});

describe('detectTheme', () => {
  it('uses the system setting when nothing was chosen', () => {
    systemPrefersDark(true);
    expect(detectTheme()).toBe('dark');
    systemPrefersDark(false);
    expect(detectTheme()).toBe('light');
  });

  it('prefers what the person chose over the system, and ignores a stale value', () => {
    systemPrefersDark(true);
    localStorage.setItem('helpdesk.theme', 'light');
    expect(detectTheme()).toBe('light');

    localStorage.setItem('helpdesk.theme', 'purple');
    expect(detectTheme()).toBe('dark');
  });

  it('copes with a browser that has no matchMedia', () => {
    window.matchMedia = undefined;
    expect(detectTheme()).toBe('light');
  });
});

describe('ThemeSwitch', () => {
  it('starts from the system theme, applies it, and offers the other one', () => {
    systemPrefersDark(true);
    render(<ThemeSwitch />);

    expect(document.documentElement.dataset.theme).toBe('dark');
    expect(screen.getByRole('button', { name: 'Switch to light mode' })).toBeInTheDocument();
  });

  it('switches the page and remembers the choice', async () => {
    render(<ThemeSwitch />);
    expect(document.documentElement.dataset.theme).toBe('light');

    await userEvent.click(screen.getByRole('button', { name: 'Switch to dark mode' }));

    expect(document.documentElement.dataset.theme).toBe('dark');
    expect(localStorage.getItem('helpdesk.theme')).toBe('dark');
    expect(screen.getByRole('button', { name: 'Switch to light mode' })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Switch to light mode' }));
    expect(document.documentElement.dataset.theme).toBe('light');
    expect(localStorage.getItem('helpdesk.theme')).toBe('light');
  });

  it('a remembered choice wins on the next visit', () => {
    localStorage.setItem('helpdesk.theme', 'dark');
    render(<ThemeSwitch />);
    expect(document.documentElement.dataset.theme).toBe('dark');
  });

  it('words its label in the chosen language', () => {
    render(<LanguageProvider initial="vi"><ThemeSwitch /></LanguageProvider>);
    expect(screen.getByRole('button', { name: 'Chuyển sang giao diện tối' })).toBeInTheDocument();
  });

  it('applyTheme sets what the stylesheet reads', () => {
    applyTheme('dark');
    expect(document.documentElement.dataset.theme).toBe('dark');
  });
});
