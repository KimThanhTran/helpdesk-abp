import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { api } from '../api';
import Attachments, { MAX_BYTES } from '../components/Attachments';
import { Action } from '../constants';
import { nameScreenshot, usePastedImages } from '../hooks';
import NewTicket from '../pages/NewTicket';

vi.mock('../notifications', () => ({ useTicketChanges: () => {} }));

const image = (name = 'image.png', type = 'image/png', size = 100) => new File([new Uint8Array(size)], name, { type });

/** What Ctrl+V does in a browser: a paste event whose clipboard holds these files (and maybe some text). */
const paste = (files = [], text = '') => {
  const event = new Event('paste', { bubbles: true, cancelable: true });
  event.clipboardData = { files, getData: () => text };
  document.body.dispatchEvent(event);
  return event;
};

beforeEach(() => {
  vi.restoreAllMocks();
  URL.createObjectURL = vi.fn(() => 'blob:fake');
  URL.revokeObjectURL = vi.fn();
});

describe('nameScreenshot', () => {
  it('names a clipboard image by the time it was taken and keeps its type and bytes', () => {
    const named = nameScreenshot(image('image.png', 'image/png', 7), new Date(2026, 10, 2, 9, 5, 3));

    expect(named.name).toBe('screenshot-20261102-090503.png');
    expect(named.type).toBe('image/png');
    expect(named.size).toBe(7);
    expect(nameScreenshot(image('x', 'image/jpeg'), new Date(2026, 0, 1, 0, 0, 0)).name).toBe('screenshot-20260101-000000.jpg');
    expect(nameScreenshot(image('x', 'image/webp')).name).toMatch(/\.webp$/);
  });
});

describe('usePastedImages', () => {
  function Probe({ onImages, enabled }) {
    usePastedImages(onImages, enabled);
    return null;
  }

  it('hands over pasted images, named, and stops the browser from also pasting them', () => {
    const onImages = vi.fn();
    render(<Probe onImages={onImages} enabled />);

    const event = paste([image()]);

    expect(onImages).toHaveBeenCalledOnce();
    expect(onImages.mock.calls[0][0][0].name).toMatch(/^screenshot-\d{8}-\d{6}\.png$/);
    expect(event.defaultPrevented).toBe(true);
  });

  it('leaves pasted text alone', () => {
    const onImages = vi.fn();
    render(<Probe onImages={onImages} enabled />);

    const event = paste([], 'just some words');

    expect(onImages).not.toHaveBeenCalled();
    expect(event.defaultPrevented).toBe(false);
  });

  it('ignores files that are not images, such as a pasted document', () => {
    const onImages = vi.fn();
    render(<Probe onImages={onImages} enabled />);

    paste([new File(['x'], 'notes.pdf', { type: 'application/pdf' })]);

    expect(onImages).not.toHaveBeenCalled();
  });

  it('does nothing while disabled and stops listening when the page goes away', () => {
    const onImages = vi.fn();
    const view = render(<Probe onImages={onImages} enabled={false} />);
    paste([image()]);
    expect(onImages).not.toHaveBeenCalled();

    view.rerender(<Probe onImages={onImages} enabled />);
    view.unmount();
    paste([image()]);
    expect(onImages).not.toHaveBeenCalled();
  });
});

describe('Pasting a screenshot on the ticket page', () => {
  const ticket = (actions) => ({ id: 't1', actions });

  it('uploads it right away, like a chosen file', async () => {
    vi.spyOn(api, 'attachments').mockResolvedValue({ items: [] });
    const upload = vi.spyOn(api, 'uploadAttachment').mockResolvedValue({});
    render(<Attachments ticket={ticket([Action.Comment])} />);
    await screen.findByText('No images yet.');

    paste([image()]);

    await waitFor(() => expect(upload).toHaveBeenCalledOnce());
    expect(upload.mock.calls[0][0]).toBe('t1');
    expect(upload.mock.calls[0][1].name).toMatch(/^screenshot-/);
    await waitFor(() => expect(api.attachments).toHaveBeenCalledTimes(2));
  });

  it('applies the same rules: nothing is sent for a pasted image that is too big', async () => {
    vi.spyOn(api, 'attachments').mockResolvedValue({ items: [] });
    const upload = vi.spyOn(api, 'uploadAttachment');
    render(<Attachments ticket={ticket([Action.Comment])} />);
    await screen.findByText('No images yet.');

    paste([image('image.png', 'image/png', MAX_BYTES + 1)]);

    expect(await screen.findByRole('alert')).toHaveTextContent(/too large/);
    expect(upload).not.toHaveBeenCalled();
  });

  it('takes several pasted images but stops at the five-image limit', async () => {
    vi.spyOn(api, 'attachments').mockResolvedValue({ items: [1, 2, 3, 4].map((n) => ({ id: `a${n}`, fileName: `${n}.png`, size: 10, visibility: 1, uploader: { name: 'alice' }, creationTime: '2026-11-02T08:00:00Z' })) });
    vi.spyOn(api, 'attachmentImage').mockResolvedValue({ blob: new Blob(['x']), filename: 'x.png' });
    const upload = vi.spyOn(api, 'uploadAttachment').mockResolvedValue({});
    render(<Attachments ticket={ticket([Action.Comment])} />);
    await screen.findByText('1.png');

    paste([image(), image()]);

    await waitFor(() => expect(upload).toHaveBeenCalledOnce()); // the fifth fits, the sixth does not
    expect(await screen.findByRole('alert')).toHaveTextContent(/at most 5/);
  });

  it('is ignored on a ticket that takes no more images', async () => {
    vi.spyOn(api, 'attachments').mockResolvedValue({ items: [] });
    const upload = vi.spyOn(api, 'uploadAttachment');
    render(<Attachments ticket={ticket([])} />);
    await waitFor(() => expect(api.attachments).toHaveBeenCalled());

    const event = paste([image()]);

    expect(upload).not.toHaveBeenCalled();
    expect(event.defaultPrevented).toBe(false);
  });
});

describe('Pasting a screenshot on the New ticket form', () => {
  beforeEach(() => {
    vi.spyOn(api, 'categories').mockResolvedValue({ items: [{ id: 'c1', name: 'IT support' }] });
    vi.spyOn(api, 'suggestion').mockResolvedValue({ categories: [] });
    vi.spyOn(api, 'createTicket').mockResolvedValue({ id: 't9' });
  });

  async function openForm() {
    render(
      <MemoryRouter initialEntries={['/new']}>
        <Routes>
          <Route path="/new" element={<NewTicket />} />
          <Route path="/tickets/:id" element={<p>ticket page</p>} />
        </Routes>
      </MemoryRouter>
    );
    await screen.findByRole('option', { name: 'IT support' });
  }

  it('adds it to the list, next to files chosen with the dialog, and uploads both on Send', async () => {
    const upload = vi.spyOn(api, 'uploadAttachment').mockResolvedValue({});
    await openForm();
    await userEvent.type(screen.getByLabelText('Title'), 'Printer jams');
    await userEvent.type(screen.getByLabelText('What is going on?'), 'Page two jams');
    await userEvent.upload(screen.getByLabelText(/Images \(optional/), image('chosen.png'), { applyAccept: false });

    paste([image()]);
    expect(await screen.findByText(/screenshot-\d{8}-\d{6}\.png/)).toBeInTheDocument();
    expect(screen.getByText(/chosen\.png/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Send' }));
    await screen.findByText('ticket page');
    expect(upload.mock.calls.map(([, file]) => file.name)).toEqual([expect.stringMatching(/chosen\.png/), expect.stringMatching(/^screenshot-/)]);
  });

  it('does not swallow pasted text, so the fields still work', async () => {
    await openForm();
    const field = screen.getByLabelText('Title');
    field.focus();

    const event = paste([], 'copied words');

    expect(event.defaultPrevented).toBe(false);
    fireEvent.change(field, { target: { value: 'copied words' } });
    expect(field).toHaveValue('copied words');
  });
});
