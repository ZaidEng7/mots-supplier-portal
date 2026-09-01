import { AcceptInvitePageBase } from '../components/AcceptInvitePageBase'
import { acceptStaffInvite } from '../api/staff'

export function AcceptStaffInvitePage() {
  return <AcceptInvitePageBase acceptInvite={acceptStaffInvite} keyPrefix="staff" />
}
