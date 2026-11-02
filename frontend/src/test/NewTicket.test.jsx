import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { api } from '../api';
import { MAX_BYTES } from '../components/Attachments';
import NewTicket from '../pages/NewTicket';

function Landing() {
  const { pathname, state } = useLocation();
  return <p>{`landed on ${pathname} notice=${state?.notice ?? 'none'}`}</p>;
}

const image = (name = 'a.png', type = 'image/png', size = 100) => new File([new Uint8Array(size)], name, { type });

beforeEach(() => {
  vi.restoreAllMocks();
  URL.createObjectURL = vi.fn(() => 'blob:fake');
  URL.revokeObjectURL = vi.fn();
  vi.spyOn(api, 'categories').mockResolvedValue({ items: [{ id: 'c1', name: 'IT support' }] });
  vi.spyOn(api, 'suggestion').mockResolvedValue({ categories: [] });
  vi.spyOn(api, 'createTicket').mockResolvedValue({ id: 't9' });
});

async function openForm() {
  render(
    <MemoryRouter initialEntries={['/new']}>
      <Routes>
        <Route path="/new" element={<NewTicket />} />
        <Route path="/tickets/:id" element={<Landing />} />
      </Routes>
    </MemoryRouter>
  );
  await screen.findByRole('option', { name: 'IT support' });
}

const fill = async () => {
  await userEvent.type(screen.getByLabelText('Title'), 'Printer jams');
  await userEvent.type(screen.getByLabelText('What is going on?'), 'Page two jams');
};
const pick = (...files) => userEvent.upload(screen.getByLabelText(/Images \(optional/), files, { applyAccept: false });

describe('New ticket with images', () => {
  it('creates the ticket first and then uploads each chosen image to it, in order', async () => {
    const upload = vi.spyOn(api, 'uploadAttachment').mockResolvedValue({});
    await openForm();
    await fill();
    const [one, two] = [image('one.png'), image('two.jpg', 'image/jpeg')];
    await pick(one, two);

    await userEvent.click(screen.getByRole('button', { name: 'Send' }));

    expect(await screen.findByText('landed on /tickets/t9 notice=none')).toBeInTheDocument();
    expect(upload.mock.calls).toEqual([['t9', one], ['t9', two]]);
    expect(api.createTicket.mock.invocationCallOrder[0]).toBeLessThan(upload.mock.invocationCallOrder[0]);
  });

  it('works without any image, as before', async () => {
    const upload = vi.spyOn(api, 'uploadAttachment');
    await openForm();
    await fill();

    await userEvent.click(screen.getByRole('button', { name: 'Send' }));

    expect(await screen.findByText(/landed on \/tickets\/t9/)).toBeInTheDocument();
    expect(upload).not.toHaveBeenCalled();
  });

  it('lists the chosen images and lets one be removed before sending', async () => {
    const upload = vi.spyOn(api, 'uploadAttachment').mockResolvedValue({});
    await openForm();
    await fill();
    await pick(image('keep.png'), image('drop.png'));
    expect(screen.getByText(/keep\.png/)).toBeInTheDocument();
    expect(screen.getByText(/drop\.png/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Remove drop.png' }));
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));

    await screen.findByText(/landed on/);
    expect(upload).toHaveBeenCalledTimes(1);
    expect(upload.mock.calls[0][1].name).toBe('keep.png');
  });

  it('refuses a file that is not an image or is too big, and keeps the good ones', async () => {
    await openForm();
    await pick(image('ok.png'), image('evil.png', 'text/html'), image('big.png', 'image/png', MAX_BYTES + 1));

    expect(screen.getByText(/ok\.png/)).toBeInTheDocument();
    expect(screen.queryByText(/evil\.png ·/)).not.toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent(/big\.png: The image is too large/);
  });

  it('takes at most five images and then closes the chooser', async () => {
    await openForm();
    await pick(...['1', '2', '3', '4', '5', '6'].map((n) => image(`${n}.png`)));

    expect(screen.getAllByRole('button', { name: /^Remove / })).toHaveLength(5);
    expect(screen.getByRole('alert')).toHaveTextContent(/6\.png: A ticket can hold at most 5 images/);
    expect(screen.getByLabelText(/Images \(optional/)).toBeDisabled();
  });

  it('keeps the ticket and says which image failed when an upload is refused', async () => {
    vi.spyOn(api, 'uploadAttachment')
      .mockResolvedValueOnce({})
      .mockRejectedValueOnce(new Error('Only PNG, JPEG and WebP images can be attached.'));
    await openForm();
    await fill();
    await pick(image('good.png'), image('bad.png'));

    await userEvent.click(screen.getByRole('button', { name: 'Send' }));

    const landed = await screen.findByText(/landed on \/tickets\/t9/);
    expect(landed).toHaveTextContent('one image could not be attached: bad.png (Only PNG, JPEG and WebP images can be attached.)');
    expect(api.createTicket).toHaveBeenCalledOnce();
  });

  it('uploads nothing when the ticket itself is refused, and keeps the images for another try', async () => {
    api.createTicket.mockRejectedValue(new Error('Title is required.'));
    const upload = vi.spyOn(api, 'uploadAttachment');
    await openForm();
    await fill();
    await pick(image('shot.png'));

    await userEvent.click(screen.getByRole('button', { name: 'Send' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Title is required.');
    expect(upload).not.toHaveBeenCalled();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Send' })).toBeEnabled());
    expect(screen.getByText(/shot\.png/)).toBeInTheDocument();
  });
});
