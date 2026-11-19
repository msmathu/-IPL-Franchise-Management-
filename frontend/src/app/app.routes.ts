import { inject } from '@angular/core';
import { CanActivateFn, Router, Routes } from '@angular/router';
import { Auth } from './core/auth';
import { authGuard, permissionGuard } from './core/auth';
import { P } from './core/services';

/** Sidebar entries; each is shown only to users holding one of its permissions. */
export interface NavItem {
  label: string;
  icon: string;
  path: string;
  perms: string[];
}

export const NAV: NavItem[] = [
  { label: 'Dashboard', icon: 'dashboard', path: '/dashboard', perms: [P.reportsRead] },
  { label: 'Franchises', icon: 'shield', path: '/franchises', perms: [P.franchisesRead] },
  { label: 'Players', icon: 'groups', path: '/players', perms: [P.playersRead] },
  { label: 'Fixtures & Table', icon: 'scoreboard', path: '/season', perms: [P.statsRead] },
  { label: 'Player Statistics', icon: 'query_stats', path: '/statistics', perms: [P.statsRead] },
  { label: 'Player Comparison', icon: 'compare_arrows', path: '/comparison', perms: [P.comparisonsRead] },
  { label: 'Trading Centre', icon: 'swap_horiz', path: '/trading', perms: [P.tradesRead] },
  { label: 'Retention & Releases', icon: 'how_to_reg', path: '/retention', perms: [P.retentionRead] },
  { label: 'Auction Centre', icon: 'gavel', path: '/auction', perms: [P.auctionsRead] },
  { label: 'Squad Management', icon: 'diversity_3', path: '/squad', perms: [P.squadRead] },
  { label: 'Injury Replacements', icon: 'healing', path: '/replacements', perms: [P.squadRead] },
  { label: 'Playing XI Builder', icon: 'sports_cricket', path: '/playing-xi', perms: [P.squadRead] },
  { label: 'Scouting Centre', icon: 'travel_explore', path: '/scouting', perms: [P.scoutingRead, P.scoutingReport] },
  { label: 'Player Valuation', icon: 'trending_up', path: '/valuation', perms: [P.valuationRead] },
  { label: 'Finance & Contracts', icon: 'account_balance', path: '/finance', perms: [P.financeRead, P.contractsRead] },
  { label: 'Communication', icon: 'forum', path: '/messages', perms: [P.messagesRead] },
  { label: 'Medical & Availability', icon: 'medical_services', path: '/medical', perms: [P.availabilityRead, P.medicalRead] },
  { label: 'Reports & Analytics', icon: 'summarize', path: '/reports', perms: [P.reportsRead] },
  { label: 'Notifications', icon: 'notifications', path: '/notifications', perms: [] },
  { label: 'User Management', icon: 'manage_accounts', path: '/users', perms: [P.usersManage, P.usersAdmin] },
  { label: 'Settings', icon: 'settings', path: '/settings', perms: [] },
  { label: 'Audit Logs', icon: 'history', path: '/audit', perms: [P.auditRead] },
];

/** Sends each user to the first area their role can open (not everyone has the dashboard). */
const homeRedirect: CanActivateFn = () => {
  const auth = inject(Auth);
  const first = NAV.find((n) => n.perms.length > 0 && auth.has(...n.perms)) ?? NAV.find((n) => n.path === '/notifications')!;
  return inject(Router).parseUrl(first.path);
};

const page = (path: string, perms: string[], load: () => Promise<any>) => ({ path, canActivate: [permissionGuard(...perms)], loadComponent: load });

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./features/auth/auth-pages').then((m) => m.LoginPage) },
  { path: 'forgot-password', loadComponent: () => import('./features/auth/auth-pages').then((m) => m.ForgotPasswordPage) },
  { path: 'reset-password', loadComponent: () => import('./features/auth/auth-pages').then((m) => m.ResetPasswordPage) },
  { path: 'accept-invitation', data: { invitation: true }, loadComponent: () => import('./features/auth/auth-pages').then((m) => m.ResetPasswordPage) },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/shell').then((m) => m.Shell),
    children: [
      { path: '', pathMatch: 'full', canActivate: [homeRedirect], children: [] },
      page('dashboard', [P.reportsRead], () => import('./features/dashboard/dashboard').then((m) => m.DashboardPage)),
      page('franchises', [P.franchisesRead], () => import('./features/franchises/franchises').then((m) => m.FranchisesPage)),
      page('franchises/:id', [P.franchisesRead], () => import('./features/franchises/franchises').then((m) => m.FranchiseDetailPage)),
      page('players', [P.playersRead], () => import('./features/players/players').then((m) => m.PlayersPage)),
      page('players/:id', [P.playersRead], () => import('./features/players/player-profile').then((m) => m.PlayerProfilePage)),
      page('statistics', [P.statsRead], () => import('./features/players/statistics').then((m) => m.StatisticsPage)),
      page('comparison', [P.comparisonsRead], () => import('./features/players/comparison').then((m) => m.ComparisonPage)),
      page('trading', [P.tradesRead], () => import('./features/trading/trading').then((m) => m.TradingPage)),
      page('trading/new', [P.tradesCreate], () => import('./features/trading/trade-editor').then((m) => m.TradeEditorPage)),
      page('trading/:id', [P.tradesRead], () => import('./features/trading/trade-detail').then((m) => m.TradeDetailPage)),
      page('retention', [P.retentionRead], () => import('./features/squad/retention').then((m) => m.RetentionPage)),
      page('auction', [P.auctionsRead], () => import('./features/auction/auctions').then((m) => m.AuctionsPage)),
      page('auction/:id', [P.auctionsRead], () => import('./features/auction/auctions').then((m) => m.AuctionSetupPage)),
      page('auction/:id/live', [P.auctionsRead], () => import('./features/auction/live-auction').then((m) => m.LiveAuctionPage)),
      page('auction/:id/results', [P.auctionsRead], () => import('./features/auction/auctions').then((m) => m.AuctionResultsPage)),
      page('season/match/:id', [P.statsRead], () => import('./features/players/scorecard').then((m) => m.ScorecardPage)),
      page('season', [P.statsRead], () => import('./features/players/season-centre').then((m) => m.SeasonCentrePage)),
      page('replacements', [P.squadRead], () => import('./features/squad/replacements').then((m) => m.ReplacementsPage)),
      page('squad', [P.squadRead], () => import('./features/squad/squad').then((m) => m.SquadPage)),
      page('playing-xi', [P.squadRead], () => import('./features/squad/playing-xi').then((m) => m.PlayingXIPage)),
      page('scouting', [P.scoutingRead, P.scoutingReport], () => import('./features/scouting/scouting').then((m) => m.ScoutingPage)),
      page('scouting/reports/:id', [P.scoutingRead, P.scoutingReport], () => import('./features/scouting/scouting').then((m) => m.ReportPage)),
      page('valuation', [P.valuationRead], () => import('./features/valuation/valuation').then((m) => m.ValuationPage)),
      page('finance', [P.financeRead, P.contractsRead], () => import('./features/finance/finance').then((m) => m.FinancePage)),
      page('messages', [P.messagesRead], () => import('./features/operations/messages').then((m) => m.MessagesPage)),
      page('messages/:id', [P.messagesRead], () => import('./features/operations/messages').then((m) => m.MessagesPage)),
      page('medical', [P.availabilityRead, P.medicalRead], () => import('./features/operations/medical').then((m) => m.MedicalPage)),
      page('reports', [P.reportsRead], () => import('./features/operations/reports').then((m) => m.ReportsPage)),
      page('notifications', [], () => import('./features/operations/notifications').then((m) => m.NotificationsPage)),
      page('users', [P.usersManage, P.usersAdmin], () => import('./features/admin/users').then((m) => m.UsersPage)),
      page('settings', [], () => import('./features/admin/settings').then((m) => m.SettingsPage)),
      page('audit', [P.auditRead], () => import('./features/admin/audit').then((m) => m.AuditPage)),
      { path: 'forbidden', loadComponent: () => import('./features/auth/auth-pages').then((m) => m.ForbiddenPage) },
      { path: '**', loadComponent: () => import('./features/auth/auth-pages').then((m) => m.NotFoundPage) },
    ],
  },
];
