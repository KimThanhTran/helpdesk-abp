import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { api } from '../api';
import Attachments, { MAX_BYTES, problemWith } from '../components/Attachments';
import { Action, Visibility } from '../constants';

vi.mock('../notifications', () => ({ useTicketChanges: () => {} }));

const ticket = (actions) => ({ id: 't1', actions });
const stored = (id, extra = {}) => ({
  id, fileName: `${id}.png`, contentType: 'image/png', size: 2048, visibility: Visibility.Public,
  uploader: { id: 'u', name: 'alice' }, creationTime: '2026-11-02T08:00:00Z', ...extra,
});
const image = (name = 'a.png', type = 'image/png', size = 100) => new File([new Uint8Array(size)], name, { type });

beforeEach(() => {
  vi.restoreAllMocks();
  URL.createObjectURL = vi.fn(() => 'blob:fake');
  URL.revokeObjectURL = vi.fn();
  vi.spyOn(api, 'attachmentImage').mockResolvedValue({ blob: new Blob(['x']), filename: 'x.png' });
});

const pick = (file) => userEvent.upload(screen.getByLabelText(/Attach an image/), file, { applyAccept: false });

describe('problemWith', () => {
  it.each([
    [image('a.png'), 0, null],
    [image('a.jpg', 'image/jpeg'), 4, null],
    [image('a.webp', 'image/webp'), 0, null],
    [image('a.gif', 'image/gif'), 0, /Only PNG, JPEG and WebP/],
    [image('evil.png', 'text/html'), 0, /Only PNG, JPEG and WebP/],
    [image('a.png', 'image/png', MAX_BYTES + 1), 0, /too large/],
    [image('a.png', 'image/png', MAX_BYTES), 0, null],
    [image('a.png', 'image/png', 0), 0, /empty/],
    [image('a.png'), 5, /at most 5/],
  ])('%#', (file, count, expected) => {
    const result = problemWith(file, count);
    if (expected === null) expect(result).toBeNull();
    else expect(result).toMatch(expected);
  });
});

describe('Attachments', () => {
  it('lists the images with who added them, and loads each one with the sign-in token', async () => {
    vi.spyOn(api, 'attachments').mockResolvedValue({ items: [stored('one'), stored('two', { visibility: Visibility.Internal })] });
    render(<Attachments ticket={ticket([Action.Comment, Action.InternalNote])} />);

    expect(await screen.findByText('one.png')).toBeInTheDocument();
    expect(screen.getByText('two.png')).toBeInTheDocument();
    expect(screen.getByText('Internal', { selector: '.badge' })).toBeInTheDocument();
    expect(await screen.findAllByRole('img')).toHaveLength(2);
    expect(api.attachmentImage).toHaveBeenCalledWith('one', expect.anything());
  });

  it('uploads an image and reloads the list', async () => {
    vi.spyOn(api, 'attachments').mockResolvedValue({ items: [] });
    const upload = vi.spyOn(api, 'uploadAttachment').mockResolvedValue(stored('new'));
    render(<Attachments ticket={ticket([Action.Comment])} />);
    await screen.findByText('No images yet.');

    const file = image('shot.png');
    await pick(file);

    expect(upload).toHaveBeenCalledWith('t1', file, false);
    await waitFor(() => expect(api.attachments).toHaveBeenCalledTimes(2));
  });

  it.each([
    ['a script renamed to .png', image('evil.png', 'text/html'), /Only PNG, JPEG and WebP/],
    ['an animated gif', image('fun.gif', 'image/gif'), /Only PNG, JPEG and WebP/],
    ['a file over 5 MB', image('big.png', 'image/png', MAX_BYTES + 1), /too large/],
  ])('refuses %s without calling the server', async (_, file, message) => {
    vi.spyOn(api, 'attachments').mockResolvedValue({ items: [] });
    const upload = vi.spyOn(api, 'uploadAttachment');
    render(<Attachments ticket={ticket([Action.Comment])} />);
    await screen.findByText('No images yet.');

    await pick(file);

    expect(await screen.findByRole('alert')).toHaveTextContent(message);
    expect(upload).not.toHaveBeenCalled();
  });

  it('shows what the server says when it refuses a file', async () => {
    vi.spyOn(api, 'attachments').mockResolvedValue({ items: [] });
    vi.spyOn(api, 'uploadAttachment').mockRejectedValue(Object.assign(new Error('Only PNG, JPEG and WebP images can be attached.'), { status: 415 }));
    render(<Attachments ticket={ticket([Action.Comment])} />);
    await screen.findByText('No images yet.');

    await pick(image());

    expect(await screen.findByRole('alert')).toHaveTextContent('Only PNG, JPEG and WebP');
  });

  it('stops offering uploads once the ticket holds five images', async () => {
    vi.spyOn(api, 'attachments').mockResolvedValue({ items: ['1', '2', '3', '4', '5'].map((id) => stored(id)) });
    render(<Attachments ticket={ticket([Action.Comment])} />);

    await screen.findByText('1.png');

    expect(screen.getByLabelText(/Attach an image/)).toBeDisabled();
  });

  it('lets an agent mark an upload as internal, and only an agent', async () => {
    vi.spyOn(api, 'attachments').mockResolvedValue({ items: [] });
    const upload = vi.spyOn(api, 'uploadAttachment').mockResolvedValue(stored('new'));
    const view = render(<Attachments ticket={ticket([Action.Comment, Action.InternalNote])} />);
    await screen.findByText('No images yet.');

    await userEvent.click(screen.getByLabelText(/Internal \(the requester/));
    const file = image();
    await pick(file);
    expect(upload).toHaveBeenCalledWith('t1', file, true);

    view.unmount();
    render(<Attachments ticket={ticket([Action.Comment])} />);
    await screen.findByText('No images yet.');
    expect(screen.queryByLabelText(/Internal \(the requester/)).not.toBeInTheDocument();
  });

  it('shows no upload box on a ticket that takes no more comments, and nothing if there is nothing to show', async () => {
    vi.spyOn(api, 'attachments').mockResolvedValue({ items: [] });
    const { container } = render(<Attachments ticket={ticket([])} />);

    await waitFor(() => expect(api.attachments).toHaveBeenCalled());
    expect(container).toBeEmptyDOMElement();
  });

  it('still shows existing images on a closed ticket, without the upload box', async () => {
    vi.spyOn(api, 'attachments').mockResolvedValue({ items: [stored('old')] });
    render(<Attachments ticket={ticket([])} />);

    expect(await screen.findByText('old.png')).toBeInTheDocument();
    expect(screen.queryByLabelText(/Attach an image/)).not.toBeInTheDocument();
  });
});
