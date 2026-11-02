import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { api } from '../api';
import ExportButton from '../components/ExportButton';
import * as download from '../download';
import { Queue } from '../pages/Lists';

vi.mock('../notifications', () => ({ useTicketChanges: () => {} }));

const file = { blob: new Blob(['x']), filename: 'helpdesk-tickets-2026-11-02.xlsx' };

beforeEach(() => {
  vi.restoreAllMocks();
  vi.spyOn(download, 'saveFile').mockImplementation(() => {});
});

describe('ExportButton', () => {
  it('fetches the file and hands it to the browser', async () => {
    const fetchFile = vi.fn().mockResolvedValue(file);
    render(<ExportButton fetchFile={fetchFile} />);

    await userEvent.click(screen.getByRole('button', { name: 'Export to Excel' }));

    await waitFor(() => expect(download.saveFile).toHaveBeenCalledWith(file));
  });

  it('shows the server message when the export is refused, and can be tried again', async () => {
    const fetchFile = vi.fn().mockRejectedValueOnce(new Error('Forbidden')).mockResolvedValue(file);
    render(<ExportButton fetchFile={fetchFile} />);

    await userEvent.click(screen.getByRole('button', { name: 'Export to Excel' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Forbidden');
    expect(download.saveFile).not.toHaveBeenCalled();

    await userEvent.click(screen.getByRole('button', { name: 'Export to Excel' }));
    await waitFor(() => expect(download.saveFile).toHaveBeenCalledOnce());
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('cannot be clicked twice while the file is being prepared', async () => {
    let finish;
    const fetchFile = vi.fn(() => new Promise((resolve) => { finish = resolve; }));
    render(<ExportButton fetchFile={fetchFile} />);

    await userEvent.click(screen.getByRole('button', { name: 'Export to Excel' }));

    expect(screen.getByRole('button', { name: 'Preparing…' })).toBeDisabled();
    finish(file);
    await waitFor(() => expect(screen.getByRole('button', { name: 'Export to Excel' })).toBeEnabled());
  });
});

describe('Queue export', () => {
  it('exports what the filters on screen show, not everything', async () => {
    vi.spyOn(api, 'queue').mockResolvedValue({ items: [], totalCount: 0 });
    const exporter = vi.spyOn(api, 'exportTickets').mockResolvedValue(file);
    render(<MemoryRouter><Queue /></MemoryRouter>);

    await userEvent.click(screen.getByRole('tab', { name: 'All' }));
    await userEvent.selectOptions(screen.getByLabelText('Status'), 'Open');
    await userEvent.selectOptions(screen.getByLabelText('Priority'), 'Urgent');
    await userEvent.click(screen.getByRole('button', { name: 'Export to Excel' }));

    await waitFor(() => expect(exporter).toHaveBeenCalled());
    expect(exporter).toHaveBeenLastCalledWith({ scope: 3, filter: '', status: '2', priority: '4' });
    expect(download.saveFile).toHaveBeenCalledWith(file);
  });
});
